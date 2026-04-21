These fixture files pin the on-disk run-file shapes that the mod has written.

- `v1-pooled-run.json`
  Legacy schema. Aggregates are keyed by card definition id and do not carry
  the per-instance resume metadata introduced later.
- `v2-per-instance-run.json`
  Legacy-but-resumable per-instance schema. Aggregates are keyed by per-instance
  card id and include the resume-only snapshots needed to rebuild numbering
  after hot reload.
- `v3-per-instance-effects-run.json`
  Legacy-but-resumable additive schema. Extends the per-instance shape with
  applied-effect summaries nested under each card aggregate.
- `v4-per-instance-effects-exhaust-run.json`
  Current schema. Adds the per-card "times exhausted" count on top of the
  v3 effect summaries.

Why these exist:

- schema work should be validated against real checked-in examples, not memory
- `v1 -> v2` is not a lossless migration, so the old pooled shape needs to stay
  visible when changing loader behavior
- additive follow-on schemas (like `v2 -> v3`) still need fixture coverage so
  "old but resumable" behavior stays intentional
- future tests can read these files directly without having to reconstruct old
  JSON by hand
