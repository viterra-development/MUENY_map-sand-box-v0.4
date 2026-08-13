# Public-Release Checklist

What must happen before this repository (or the deployed site) goes public.
Items are ordered; the first four are blocking.

## 1. Regenerate CRIS data (BLOCKING)

The severity-code mapping fix (commit `eeab81b`) means every file in
`MapSandBox/wwwroot/cris-data/` currently carries wrong severity labels and
derived scores. Re-run `CrisDataProcessor` against the CRIS CSV export
(update the input paths in `CrisDataProcessor/appsettings.json` first) and
commit the regenerated outputs. Do not deploy publicly before this.

## 2. Rewrite git history (BLOCKING)

Owner PII, internal notes, and debug artifacts were removed from the tip but
remain in history (~10 historical versions of each parcel file; the pack is
33 MB mostly because of data churn). Rewrite before the repo is public:

```bash
pip install git-filter-repo
git filter-repo \
  --invert-paths \
  --path MapSandBox/wwwroot/sample-data/county-cad-parcel-test.geojson \
  --path primal-graph-debug.txt \
  --path TestOutput \
  --path "Documentation/Incremental Plans/data_access_notes.md" \
  --path PARKER-CAD-GIS.md \
  --path MapSandBox/wwwroot/txdot-city-boundaries.geojson.backup
```

The 12 `*-parcels-with-trips.geojson` files need their **historical versions**
dropped too (the current versions are clean). The simplest safe approach is to
also `--invert-paths` each parcel file and then re-add the current cleaned
copies in a follow-up commit. While rewriting, fix the 5 commits authored as
`spencerhodge@Spencers-MacBook-Pro.local` with `--mailmap`.

After the rewrite: force-push, have every collaborator re-clone, and treat the
old clone as the PII backup of record (or delete it).

- [ ] Confirm Logan Cannon consents to public attribution in the history

## 3. Resolve licensing (BLOCKING)

- [ ] ITE rate table (`TripGenProcessor/Models/IteRateModels.cs`): remove,
      license, or replace (see DATA-ATTRIBUTION.md)
- [ ] TxDOT roads "non-commercial license" question
- [ ] TCDS/MS2 scraped traffic counts: review terms; TCDS offers registered
      CSV export as a legitimate channel. Decide whether `TCDS.Importer`'s
      scraper (which uses randomized human-like delays and automation-hiding
      browser flags) should ship in a public repo at all.
- [ ] Choose a code LICENSE and add the file
- [ ] Add "© OpenStreetMap contributors" credit to the app UI

## 4. Cloudflare configuration (BLOCKING for the deployed site)

- [ ] `wrangler secret put` the ten GOOGLE_FORM_* values (see wrangler.toml) —
      never commit them as [vars]
- [ ] Add a WAF rate-limiting rule on `POST /api/log-visit` (the in-Worker
      limiter is best-effort only)
- [ ] Publish a privacy notice before enabling the Google Form forwarding
      (PRIVACY.md is a starting draft; link it from the app footer)

## 5. Repo/CI cleanup

- [ ] Pick ONE deploy target: Cloudflare (`deploy-cloudflare.yml`) or Azure
      SWA (`azure-static-web-apps-*.yml`); delete the other workflow
- [ ] Decide whether `.devcontainer/` (installs the Azure DevOps private-feed
      credential provider) and `.cursorignore` should ship
- [ ] Confirm the Azure storage/Front Door hostnames in
      `MapSandBox/wwwroot/appsettings.json` are safe to expose (they are
      anonymous-read by design; check egress cost tolerance)
- [ ] Confirm `spencer@hodgetx.com` in
      `TripGenProcessor/scripts/enrich_parcels_with_osm.py` (Overpass
      User-Agent etiquette) is intended to be public

## 6. Remaining code findings (post-launch acceptable)

Tracked from the Aug 2026 review; not blocking but worth scheduling:
- Hydroplaning detection is dead code (ContributingFactors never populated)
- `new Function()` eval sink for `@@=` layer-config strings in js/map.js —
  becomes code injection if layer configs ever load from fetched JSON
- CrisDataProcessor/NoaaDataProcessor appsettings still use absolute
  `/workspaces/...` input paths (documented, but unfriendly)
- TypeBasedTrafficMatcher measures distance to road vertices rather than
  segments (lowers match confidence on long straight segments)
- 690 generated tile files tracked in `wwwroot/tiles/` (regenerable via
  `generate-tiles.sh`; consider generating at deploy time instead)
