package host

import (
	"archive/zip"
	"io"
	"os"
	"path/filepath"
	"strings"
)

// PackEvidence builds a single zip of the verification verdict + evidence so the
// pod can pull it with one ScpPull (remotehost has no directory pull). The zip
// lands the exact finalizer tree when unpacked into the pod's artifacts dir:
//
//	verification.json
//	screenshots/<name>
//	evidence/<live-mcp-*.json>
//
// NOTE [SDK gap, flagged for the hub]: remotehost has ScpPushTree (push a dir)
// and ScpPull (pull a file) but no ScpPullTree, so directory evidence is packed
// host-side and pulled as one archive.
func PackEvidence(workingDir, outZip string) error {
	artifactDir := filepath.Join(workingDir, "sts2-artifacts")
	screenshotDir := filepath.Join(workingDir, "sts2-screenshots")

	f, err := os.Create(outZip)
	if err != nil {
		return err
	}
	defer f.Close()
	zw := zip.NewWriter(f)
	defer zw.Close()

	// verification.json (and verification.md if present).
	for _, name := range []string{"verification.json", "verification.md"} {
		_ = addFileToZip(zw, filepath.Join(artifactDir, name), name)
	}
	// screenshots/*
	if entries, err := os.ReadDir(screenshotDir); err == nil {
		for _, e := range entries {
			if !e.IsDir() {
				_ = addFileToZip(zw, filepath.Join(screenshotDir, e.Name()), "screenshots/"+e.Name())
			}
		}
	}
	// evidence/<live-mcp-*.json> — the verifier's captured get_game_state JSON.
	if entries, err := os.ReadDir(artifactDir); err == nil {
		for _, e := range entries {
			if !e.IsDir() && strings.HasPrefix(e.Name(), "live-mcp-") && strings.HasSuffix(e.Name(), ".json") {
				_ = addFileToZip(zw, filepath.Join(artifactDir, e.Name()), "evidence/"+e.Name())
			}
		}
	}
	return nil
}

func addFileToZip(zw *zip.Writer, src, name string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	w, err := zw.Create(name)
	if err != nil {
		return err
	}
	_, err = io.Copy(w, in)
	return err
}
