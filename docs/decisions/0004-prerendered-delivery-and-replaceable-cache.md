# 0004: Prerendered delivery and replaceable cache

Accepted implementation starting point, 30 September 2026.

The user requested a delivery pipeline and a simple substitutable cache for the demo. They also asked whether Static or Prerendered is the clearer counterpart to Dynamic pages, and whether dynamic pages can update the DOM without a reload.

Use Prerendered for ahead-of-request HTML and Dynamic for request-time rendering. Interactivity is independent of rendering mode: either can use approved browser scripts. Static remains an appropriate description of file exports. Dynamic rendering and browser interactions are not implemented in this change.

Extract delivery contracts from the management route into `VibePress.Delivery`. Keep the memory-cache implementation in a separate package project and allow a no-cache replacement through configuration. Use release/file identity in cache keys and read one release per request. Header hooks run on hits and misses; they cannot transform reviewed body bytes or override mandatory headers.

Retain loopback-only Development opt-in and browser/proxy no-store for the demo. Do not expose this discovery host as a customer public service. Independent hosting, durable promotion and runtime rendering policies belong to subsequent release work.

The initial cache stores prepared representations of already-rendered files. It proves modularity and correctness, not a performance improvement. Distributed stores, expensive render caching, outage resilience and HTTP/CDN cache policies require explicit follow-up designs and measurements. Current verification covers source switching/restoration, cache bypass/substitution and actual HTTP responses.
