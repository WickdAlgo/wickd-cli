# Changelog

All notable changes to the public WickdAlgo CLI are recorded here.

## Unreleased

### Removed

- Drop the no-op `-v` / `--verbose` global option and its README listing.
- Remove unused `RenderBanner` / `RenderInfo` renderer surface.
- Remove unused `Microsoft.Extensions.Configuration*` package references.
- Change dataset-alias delete to `Task` and drop the unreachable "not found" branch.

### Documentation

- Record the maintainer-provided attribution for the initial thin-client
  implementation merged through cli#1–#4: generated with Antigravity
  (Gemini). Those commits predate the repository's attribution-footer
  convention and remain authored by Burak; this entry records provenance
  without rewriting Git history.
