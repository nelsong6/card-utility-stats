package pod

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"io"
	"net/http"
	"strings"
	"time"

	"github.com/romaine-life/glimmung/harness/step"
)

// mintGitHubToken mints the per-attempt GitHub token via the run callback URL
// glimmung bakes onto the pod (GLIMMUNG_GITHUB_TOKEN_URL + the
// X-Glimmung-Attempt-Token header), mirroring lib.sh's native_mint_github_token.
//
// NOTE [SDK gap, flagged for the hub]: harness/remotehost mints the ssh cert and
// tailscale authkey from the run callbacks but not the GitHub token, so this
// callback mint lives in the pod. A harness/runcallbacks (or remotehost) helper
// would let every consumer share it.
func mintGitHubToken(ctx context.Context, c *step.Context) (string, *step.LayeredError) {
	url := strings.TrimSpace(c.Env("GLIMMUNG_GITHUB_TOKEN_URL"))
	attempt := strings.TrimSpace(c.Env("GLIMMUNG_ATTEMPT_TOKEN"))
	if url == "" || attempt == "" {
		return "", step.HarnessError("github_token_misconfigured", "GLIMMUNG_GITHUB_TOKEN_URL / GLIMMUNG_ATTEMPT_TOKEN not set", nil)
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, nil)
	if err != nil {
		return "", step.HarnessError("github_token_request", "build token request", err)
	}
	req.Header.Set("X-Glimmung-Attempt-Token", attempt)
	client := &http.Client{Timeout: 30 * time.Second}
	resp, err := client.Do(req)
	if err != nil {
		return "", step.HostError("github_token_request", "github token endpoint request failed", err)
	}
	defer resp.Body.Close()
	buf := new(bytes.Buffer)
	_, _ = io.Copy(buf, resp.Body)
	if resp.StatusCode >= 400 {
		return "", step.HostError("github_token_request", "github token endpoint returned HTTP "+resp.Status, nil)
	}
	var doc struct {
		Token string `json:"token"`
	}
	if err := json.Unmarshal(buf.Bytes(), &doc); err != nil {
		return "", step.HarnessError("github_token_request", "github token endpoint returned invalid JSON", err)
	}
	if strings.TrimSpace(doc.Token) == "" {
		return "", step.HarnessError("github_token_request", "github token endpoint returned no usable .token", nil)
	}
	return strings.TrimSpace(doc.Token), nil
}

func base64Token(token string) string {
	return base64.StdEncoding.EncodeToString([]byte(token))
}

// httpNewRequest builds an authenticated GitHub API GET.
func httpNewRequest(ctx context.Context, url, token string) (*http.Request, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Authorization", "token "+token)
	return req, nil
}

func httpDo(req *http.Request) (*http.Response, error) {
	client := &http.Client{Timeout: 30 * time.Second}
	return client.Do(req)
}
