package pod

import "github.com/romaine-life/glimmung/harness/step"

// Registry builds the pod-face step.Registry: one handler per workflow step
// slug across the prepare (env-prep), llm-work, llm-verify, and cleanup phases.
// The slugs are identical to the retired scripts/glimmung-native/*.sh dispatch
// so the registered workflow's phase shape is untouched by the migration.
func Registry() *step.Registry {
	r := step.NewRegistry()
	r.Register(prepareHandlers()...)
	r.Register(llmWorkHandlers()...)
	r.Register(llmVerifyHandlers()...)
	r.Register(cleanupHandlers()...)
	return r
}

// Slugs returns the registered pod step slugs (sorted), for the hand-off mapping
// and the registry-completeness test.
func Slugs() []string { return Registry().Slugs() }
