package host

import (
	"os"
	"path/filepath"
	"strings"
)

// StageFinalizerEvidence copies the run's screenshot and live-mcp evidence into
// the finalizer artifact tree verification.WriteFinalizable created under
// workingDir, so the complete tree
//
//	artifacts/verification.json
//	artifacts/screenshots/<name>
//	artifacts/evidence/<live-mcp-*.json>
//
// can be pulled back to the pod with one recursive ScpPullTree (SDK v0.2.0). It
// replaces the retired host-side zip pack + pod-side unzip: the host no longer
// archives evidence, and the pod no longer unpacks it.
//
// Screenshots come from sts2-screenshots/ (the agent's SCREENSHOT_DIR) and the
// captured get_game_state JSON from sts2-artifacts/live-mcp-*.json (the agent's
// VALIDATION_ARTIFACT_DIR), remapped into the finalizer's screenshots/ and
// evidence/ subdirs.
func StageFinalizerEvidence(workingDir string) error {
	artifacts := filepath.Join(workingDir, "artifacts")
	screenshotsDst := filepath.Join(artifacts, "screenshots")
	evidenceDst := filepath.Join(artifacts, "evidence")
	for _, dir := range []string{screenshotsDst, evidenceDst} {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return err
		}
	}

	srcScreens := filepath.Join(workingDir, "sts2-screenshots")
	if entries, err := os.ReadDir(srcScreens); err == nil {
		for _, e := range entries {
			if e.IsDir() {
				continue
			}
			if err := copyFile(filepath.Join(srcScreens, e.Name()), filepath.Join(screenshotsDst, e.Name())); err != nil {
				return err
			}
		}
	}

	srcArtifacts := filepath.Join(workingDir, "sts2-artifacts")
	if entries, err := os.ReadDir(srcArtifacts); err == nil {
		for _, e := range entries {
			if e.IsDir() {
				continue
			}
			if strings.HasPrefix(e.Name(), "live-mcp-") && strings.HasSuffix(e.Name(), ".json") {
				if err := copyFile(filepath.Join(srcArtifacts, e.Name()), filepath.Join(evidenceDst, e.Name())); err != nil {
					return err
				}
			}
		}
	}
	return nil
}
