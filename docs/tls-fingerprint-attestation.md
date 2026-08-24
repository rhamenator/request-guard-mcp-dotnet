# TLS Fingerprint Attestation

JA3/JA4 fields supplied by an ordinary client are untrusted and do not affect rules or cache
identity. A trusted TLS terminator may sign them with HMAC-SHA256 using a distinct shared key of at
least 32 UTF-8 bytes.

The token is `v1:<unix-seconds>:<64-lowercase-hex-HMAC>`. The HMAC input is the following UTF-8
fields joined by a single newline, with no trailing newline:

```text
v1
<unix-seconds>
<trimmed lowercase client IP>
<trimmed uppercase method>
<path>
<normalized JA3 or empty>
<normalized JA4 or empty>
<normalized source>
```

JA3 is 32 hexadecimal characters. JA4 is a ten-character alphanumeric prefix followed by two
12-hex sections separated by underscores. Source is 1–32 ASCII letters, digits, `_`, or `-`.
Newline, carriage return, and NUL are forbidden in canonical fields. Verification uses fixed-time
comparison and accepts timestamps within the configured age in either clock direction.

Configure `TLS_FINGERPRINT_ATTESTATION_KEY` on verifiers and trusted producers. During rotation,
put the old key in `TLS_FINGERPRINT_ATTESTATION_PREVIOUS_KEY`; only the primary key should sign new
tokens. Never reuse a Bearer token, database password, or cache-scope HMAC key. The server clears the
attestation before caching or persistence.

The cross-implementation test vector is:

```text
key: 0123456789abcdef0123456789abcdef
issued: 1700000000
ip: 198.51.100.7
method: GET
path: /products
JA3: 72a589da586844d7f0818ce684948eea
JA4: t13d1516h2_8daaf6152771_e5627efa2ab1
source: envoy
token: v1:1700000000:192976122c9fbaa4cb8c2554be66f2439e020a7d470ac838f2a622b0c5829a49
```
