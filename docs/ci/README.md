# Pending CI workflow (hub-applied)

`go-harness-quality.yml` here is the replacement CI gate for the retired
`ps-quality.yml` + `glimmung-native-quality.yml` (both deleted in this PR). It
runs `go build` (Linux pod face), `GOOS=windows GOARCH=amd64 go build` (host
face cross-compile), `go vet`, and `go test ./...` for the new run-harness
module.

It lives here, not under `.github/workflows/`, only because the governed-git
wall blocks the session App token from creating workflow files (no `workflows`
permission). The hub (or a human with `workflows` permission) installs it with:

    git mv docs/ci/go-harness-quality.yml .github/workflows/go-harness-quality.yml

No other change is needed — the file is final.
