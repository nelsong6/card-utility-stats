package host

import (
	"path/filepath"
	"testing"
)

func TestResolveClaudeCLIPath(t *testing.T) {
	want := `D:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe`
	orig := fileExists
	defer func() { fileExists = orig }()
	fileExists = func(p string) bool { return p == want }

	got, err := ResolveClaudeCLIPath()
	if err != nil || got != want {
		t.Fatalf("got %q err %v", got, err)
	}

	fileExists = func(string) bool { return false }
	if _, err := ResolveClaudeCLIPath(); err == nil {
		t.Error("expected error when no candidate exists")
	}
}

func TestResolveSts2GameDir(t *testing.T) {
	orig := fileExists
	defer func() { fileExists = orig }()
	want := `D:\SteamLibrary\steamapps\common\Slay the Spire 2`
	marker := filepath.Join(want, "data_sts2_windows_x86_64", "sts2.dll")
	fileExists = func(p string) bool { return p == marker }
	got, err := ResolveSts2GameDir()
	if err != nil || got != want {
		t.Fatalf("got %q err %v", got, err)
	}
}

func TestBridgeURLAndPing(t *testing.T) {
	if BridgeURL("127.0.0.1", 15526) != "http://127.0.0.1:15526/" {
		t.Errorf("unexpected bridge URL: %s", BridgeURL("127.0.0.1", 15526))
	}
	orig := bridgeHTTPGet
	defer func() { bridgeHTTPGet = orig }()
	bridgeHTTPGet = func(string) (int, []byte, error) { return 200, []byte(`{"status":"ok"}`), nil }
	if !BridgePingOK("127.0.0.1", 15526) {
		t.Error("expected ok ping")
	}
	bridgeHTTPGet = func(string) (int, []byte, error) { return 200, []byte(`{"status":"starting"}`), nil }
	if BridgePingOK("127.0.0.1", 15526) {
		t.Error("non-ok status must not ping ok")
	}
	bridgeHTTPGet = func(string) (int, []byte, error) { return 500, nil, nil }
	if BridgePingOK("127.0.0.1", 15526) {
		t.Error("5xx must not ping ok")
	}
}
