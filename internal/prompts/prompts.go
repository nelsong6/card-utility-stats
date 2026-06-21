// Package prompts builds the per-phase Claude prompts and holds the phase
// definitions (tool allow/deny lists, allowed abort enums, timeouts, budgets,
// artifact filenames) ported from run-phases.ps1.
//
// The defining property is laziness: run-phases.ps1 built ALL THREE phase
// prompts eagerly before selecting one (failure mode #3 — a verify-only run
// still constructed the test-plan prompt). BuildPrompt constructs ONLY the
// requested phase's prompt; the lazy-build regression test proves a
// verification run never touches the test-plan template.
package prompts

import (
	"bytes"
	"embed"
	"fmt"
	"text/template"
)

//go:embed templates/*.tmpl
var templateFS embed.FS

// Params are the substitutions every phase prompt needs.
type Params struct {
	IssueNumber           string
	RepoSlug              string
	RepoRoot              string
	McpConfigPath         string
	ValidationArtifactDir string
	ScreenshotDir         string
}

// templateData is the rendering context for a single template.
type templateData struct {
	Params
	PhaseName        string
	IncludeIssueRead bool
}

// promptBuilderObserver, when non-nil, is notified of every phase template that
// is actually rendered. It exists solely so the lazy-build regression test can
// prove a verification run never constructs the test-plan prompt (#3). It is nil
// and zero-cost in production.
var promptBuilderObserver func(name string)

func observe(name string) {
	if promptBuilderObserver != nil {
		promptBuilderObserver(name)
	}
}

// Phases in run order. The verification phase deliberately does NOT include the
// issue read (it works from the handoff artifacts).
const (
	PhaseTestPlan       = "test_plan"
	PhaseImplementation = "implementation"
	PhaseVerification   = "verification"
)

// includeIssueRead reports whether a phase reads the GitHub issue directly.
func includeIssueRead(phase string) bool { return phase != PhaseVerification }

// BuildPrompt renders ONLY the requested phase's prompt: the common prefix plus
// that phase's rules body. An unknown phase is an error. Nothing else is
// rendered — the test-plan template is never touched by a verification build.
func BuildPrompt(phase string, p Params) (string, error) {
	bodyTemplate, ok := map[string]string{
		PhaseTestPlan:       "templates/test_plan.tmpl",
		PhaseImplementation: "templates/implementation.tmpl",
		PhaseVerification:   "templates/verification.tmpl",
	}[phase]
	if !ok {
		return "", fmt.Errorf("unknown phase %q", phase)
	}

	data := templateData{Params: p, PhaseName: phase, IncludeIssueRead: includeIssueRead(phase)}
	prefix, err := render("templates/common.tmpl", data)
	if err != nil {
		return "", err
	}
	observe("common")
	body, err := render(bodyTemplate, data)
	if err != nil {
		return "", err
	}
	observe(phase)
	return prefix + body, nil
}

func render(name string, data templateData) (string, error) {
	tmpl, err := template.New(name).ParseFS(templateFS, name)
	if err != nil {
		return "", fmt.Errorf("parse template %s: %w", name, err)
	}
	var buf bytes.Buffer
	// ParseFS names the template by its base path; execute the embedded file.
	if err := tmpl.ExecuteTemplate(&buf, baseName(name), data); err != nil {
		return "", fmt.Errorf("execute template %s: %w", name, err)
	}
	return buf.String(), nil
}

func baseName(path string) string {
	for i := len(path) - 1; i >= 0; i-- {
		if path[i] == '/' {
			return path[i+1:]
		}
	}
	return path
}
