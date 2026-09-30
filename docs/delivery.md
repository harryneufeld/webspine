# Delivery pipeline: first implemented slice

30 September 2026. This module is currently mounted on the optional, loopback-only Development demo. It is not a deployed production public host or authenticated customer preview system.

## Page terminology

Use **Prerendered** for HTML generated ahead of a visitor request and **Dynamic** for HTML rendered on request. Static remains useful when describing an exported set of files, but can be misunderstood as meaning an unchanging or noninteractive website. Either page mode can run browser JavaScript and update the DOM without navigation. Interactivity and rendering mode are independent choices.

The demo currently uses prerendered pages without browser JavaScript. Dynamic rendering and interactive components are not implemented in this slice. Future scripts must be approved design/plugin assets with an appropriate CSP, not executable CMS content.

## Request flow

The host enforces its access boundary before calling `PrerenderedDelivery`. The pipeline accepts GET/HEAD, validates the artifact-relative route, captures one active artifact, resolves its file, optionally looks up a prepared representation, invokes ordered header contributors, applies mandatory HTTP/security headers, checks conditional validators and sends unchanged artifact bytes (or an empty HEAD/304 response).

`IPublishedArtifactSource` supplies the artifact. The demo uses a fixed source created at startup. Tests substitute a source that changes and restores artifacts; this proves cache namespace behavior, not a completed publication/rollback workflow. A future production source must read an atomically promoted retained release without relying on management availability.

`IDeliveryHeaders` runs on cache hits and misses. It can add safe custom X- headers, but cannot replace cache, cookie, protocol or security headers. The sample header identifies prerendered delivery. This is an intentionally narrow first seam; request routing, access policy, dynamic rendering and telemetry hooks will be added with their owning services. These are trusted compiled extensions, not a sandbox.

## Replaceable cache

`IDeliveryCache` lives in the delivery contract. `Webspine.Caching.Memory` is a separate implementation package using the .NET memory-cache library from the ASP.NET Core shared framework. No Redis, separate server or extra package installation is needed. The package also supplies a no-cache implementation, selected with `--Demo:CacheEnabled false`.

Keys contain release digest, canonical artifact file path and file digest. Each request resolves the release before cache lookup. Switching/restoring releases selects a different namespace rather than relying on a global purge. Unreachable entries expire after five minutes; the local cache has a 16 MiB logical size budget, including a per-entry allowance. This is an eviction budget, not a precise process-memory ceiling.

Credential, authenticated, cookie and query-string requests bypass shared caching. Future request variations must be explicitly modeled in cache keys/policies before caching dynamic or personalized output. Customer draft previews must stay separate from this fictional demo cache.

The cache stores prepared response representations. HTML is already prerendered and retained; this slice is infrastructure evidence, not a measured rendering-speed improvement. Expensive dynamic rendering, concurrent-miss suppression, shared-store invalidation and cache-outage recovery remain future work. Cache-provider errors currently fail the request; no resilience guarantee is claimed.

## HTTP and operational limits

The demo keeps browser/proxy `Cache-Control: no-store`, even while using an internal cache for fictional public sample data. It sends content-based ETags, supports GET/HEAD conditional requests and never changes artifact body bytes. Diagnostic cache hit/miss/bypass headers are enabled only by the demo host. Production browser/CDN policies, immutable asset URLs, compression and observability are not configured yet.

Public delivery is a logical module today, hosted within management for discovery. Separately running delivery, durable artifacts, deployment, management-downtime acceptance and dynamic release-bundle review rules remain release work. Dynamic output cannot generally promise the same bytes for every visitor; approval must bind its code, content, configuration and declared runtime dependencies instead.

Run both console check projects using the commands in the README. Delivery checks cover exact bytes, hooks on cache hits/misses, release changes/restoration, private/variant bypass, HEAD/ETag, bad paths/methods, cache substitution and mandatory header protection. HTTP checks cover the actual demo host with caching enabled and disabled.
