package host

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"time"
)

func basicAuth(user, token string) string {
	return base64.StdEncoding.EncodeToString([]byte(user + ":" + token))
}

// bridgeHTTPGet is overridable in tests.
var bridgeHTTPGet = func(url string) (int, []byte, error) {
	client := &http.Client{Timeout: 2500 * time.Millisecond}
	resp, err := client.Get(url)
	if err != nil {
		return 0, nil, err
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	return resp.StatusCode, body, nil
}

// BridgeURL is the SpireLensMcp bridge health URL (restart-sts2.ps1 pinged "/").
func BridgeURL(host string, port int) string {
	return fmt.Sprintf("http://%s:%d/", host, port)
}

// BridgePingOK reports whether the bridge responds with {"status":"ok"}.
func BridgePingOK(host string, port int) bool {
	code, body, err := bridgeHTTPGet(BridgeURL(host, port))
	if err != nil || code < 200 || code >= 300 {
		return false
	}
	var doc struct {
		Status string `json:"status"`
	}
	if json.Unmarshal(body, &doc) != nil {
		return false
	}
	return doc.Status == "ok"
}

// sts2ProcessImages are the image names restart-sts2.ps1 stopped.
var sts2ProcessImages = []string{"SlayTheSpire2.exe", "crashpad_handler.exe"}

// StopSts2 force-stops the STS2 processes and waits for the bridge to go quiet,
// mirroring restart-sts2.ps1's Stop-Sts2. Missing processes are not an error
// (idempotent teardown). Process selection is by image name; the laptop runs
// only the user's STS2 (AGENTS.md mod policy), so an image-name match is the
// host's single live instance.
func StopSts2(ctx context.Context, w io.Writer, host string, port, shutdownTimeoutSeconds int) error {
	for _, image := range sts2ProcessImages {
		// taskkill returns non-zero when the image is not running; that is fine.
		_, _ = runCmd(ctx, w, "", nil, "taskkill", "/F", "/T", "/IM", image)
	}
	deadline := time.Now().Add(time.Duration(shutdownTimeoutSeconds) * time.Second)
	for time.Now().Before(deadline) {
		if !BridgePingOK(host, port) {
			return nil
		}
		time.Sleep(time.Second)
	}
	// Best-effort: the bridge may still be responding, but teardown is advisory.
	return nil
}

// StartSts2 launches STS2, mirroring restart-sts2.ps1's Start-Sts2 priority:
// a configured scheduled task, then SlayTheSpire2.exe, then launch_opengl.bat.
func StartSts2(ctx context.Context, w io.Writer, gameDir, launchArgs string) error {
	if task := os.Getenv("SPIRELENS_HOST_STS2_LAUNCH_TASK"); task != "" {
		return mustRun(ctx, w, "", nil, "start STS2 via scheduled task", "schtasks", "/Run", "/TN", task)
	}
	exe := gameDir + `\SlayTheSpire2.exe`
	if fileExists(exe) {
		cmd := exec.Command(exe, splitArgs(launchArgs)...)
		cmd.Dir = gameDir
		return cmd.Start()
	}
	bat := gameDir + `\launch_opengl.bat`
	if fileExists(bat) {
		cmd := exec.Command(bat)
		cmd.Dir = gameDir
		return cmd.Start()
	}
	return fmt.Errorf("no STS2 launcher found under %s (SlayTheSpire2.exe / launch_opengl.bat) and SPIRELENS_HOST_STS2_LAUNCH_TASK is unset", gameDir)
}

// WaitSts2Ready polls the bridge until it returns {"status":"ok"} or the
// deadline passes, mirroring Wait-Sts2Ready.
func WaitSts2Ready(host string, port, startupTimeoutSeconds int) error {
	deadline := time.Now().Add(time.Duration(startupTimeoutSeconds) * time.Second)
	for time.Now().Before(deadline) {
		if BridgePingOK(host, port) {
			return nil
		}
		time.Sleep(3 * time.Second)
	}
	return fmt.Errorf("STS2 bridge did not become ready on %s within %ds", BridgeURL(host, port), startupTimeoutSeconds)
}

func splitArgs(s string) []string {
	out := []string{}
	for _, f := range fieldsQuoteAware(s) {
		if f != "" {
			out = append(out, f)
		}
	}
	return out
}

// fieldsQuoteAware splits on spaces (the launch args here are simple flags like
// "--rendering-driver opengl3").
func fieldsQuoteAware(s string) []string {
	cur := ""
	out := []string{}
	for _, r := range s {
		if r == ' ' {
			if cur != "" {
				out = append(out, cur)
				cur = ""
			}
			continue
		}
		cur += string(r)
	}
	if cur != "" {
		out = append(out, cur)
	}
	return out
}
