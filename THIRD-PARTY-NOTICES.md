# Third-party notices

## QR Code Generator for JavaScript (qrcode-generator 2.0.4)

Used in `src/IntakeServer/wwwroot/staff/qr.js` to draw QR codes for the patient-form posters.
A small helper (`qrSvg`) that renders the code as SVG with DOM APIs was appended by this project.

Copyright (c) 2009 Kazuhiko Arase — https://github.com/kazuhikoarase/qrcode-generator

The word "QR Code" is a registered trademark of DENSO WAVE INCORPORATED.

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Cloudflare Tunnel (cloudflared) — optional, not bundled

`deploy/install-server.ps1 -WithTunnel` downloads the official `cloudflared` binary from
https://github.com/cloudflare/cloudflared/releases (Apache License 2.0). Use of Cloudflare services is subject to
Cloudflare's own terms.
