These fixture files pin the on-disk run-file shapes that the mod has written.

- `v1-pooled-run.json`
  Legacy schema. Aggregates are keyed by card definition id and do not carry
  the per-instance resume metadata introduced later.
- `v2-per-instance-run.json`
  Current schema. Aggregates are keyed by per-instance card id and include the
  resume-only snapshots needed to rebuild numbering after hot reload.

Why these exist:

- schema work should be validated against real checked-in examples, not memory
- `v1 -> v2` is not a lossless migration, so the old pooled shape needs to stay
  visible when changing loader behavior
- future tests can read these files directly without having to reconstruct old
  JSON by hand
