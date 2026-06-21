package pod

import (
	"context"
	"encoding/base64"
	"net/http"
	"time"
)

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
