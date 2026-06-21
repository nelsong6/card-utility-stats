package host

import (
	"fmt"
	"os"
	"path/filepath"

	"github.com/romaine-life/spirelens/internal/mcpconfig"
)

// fileExists is overridable in tests.
var fileExists = func(path string) bool {
	if path == "" {
		return false
	}
	_, err := os.Stat(path)
	return err == nil
}

// claudeCandidates returns the ordered claude.exe search path, mirroring
// native-runtime.ps1's Resolve-ClaudeCliPath.
func claudeCandidates() []string {
	return dedupe([]string{
		os.Getenv("CLAUDE_CLI_PATH"),
		os.Getenv("CONFIGURED_CLAUDE_CLI_PATH"),
		`D:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe`,
		`C:\automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe`,
		filepath.Join(os.Getenv("USERPROFILE"), `automation\claude-code\node_modules\@anthropic-ai\claude-code\bin\claude.exe`),
		filepath.Join(os.Getenv("APPDATA"), `npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe`),
	})
}

// ResolveClaudeCLIPath returns the first existing claude.exe candidate.
func ResolveClaudeCLIPath() (string, error) {
	for _, c := range claudeCandidates() {
		if fileExists(c) {
			return c, nil
		}
	}
	return "", fmt.Errorf("could not resolve claude.exe; set CLAUDE_CLI_PATH or CONFIGURED_CLAUDE_CLI_PATH")
}

// sts2Candidates returns the ordered STS2 install search path, mirroring
// native-runtime.ps1's Resolve-Sts2GameDir (env, then the embedded .mcp.json
// template's STS2_GAME_DIR, then the well-known Steam install locations).
func sts2Candidates() []string {
	return dedupe([]string{
		os.Getenv("SPIRELENS_HOST_STS2_GAME_DIR"),
		os.Getenv("CONFIGURED_STS2_GAME_DIR"),
		mcpconfig.GameDirFromTemplate(),
		`D:\Programs\SteamLibrary\steamapps\common\Slay the Spire 2`,
		`D:\SteamLibrary\steamapps\common\Slay the Spire 2`,
		`C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`,
		`C:\Program Files\Steam\steamapps\common\Slay the Spire 2`,
	})
}

// sts2DLLRelative is the marker that validates a real STS2 install.
const sts2DLLRelative = `data_sts2_windows_x86_64\sts2.dll`

// ResolveSts2GameDir returns the first candidate whose data_sts2_windows_x86_64\
// sts2.dll exists.
func ResolveSts2GameDir() (string, error) {
	for _, c := range sts2Candidates() {
		if c == "" {
			continue
		}
		if fileExists(filepath.Join(c, "data_sts2_windows_x86_64", "sts2.dll")) {
			return c, nil
		}
	}
	return "", fmt.Errorf("could not resolve the STS2 install dir (no data_sts2_windows_x86_64/sts2.dll under any candidate); set SPIRELENS_HOST_STS2_GAME_DIR")
}

// Sts2DataDir returns the STS2 data dir under a game dir.
func Sts2DataDir(gameDir string) string {
	return filepath.Join(gameDir, "data_sts2_windows_x86_64")
}

func dedupe(values []string) []string {
	seen := map[string]bool{}
	out := []string{}
	for _, v := range values {
		if v == "" || seen[v] {
			continue
		}
		seen[v] = true
		out = append(out, v)
	}
	return out
}
