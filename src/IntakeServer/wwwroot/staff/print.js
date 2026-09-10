(function () {
  'use strict';
  var params = new URLSearchParams(location.search);
  var url = params.get('url') || '';
  var title = params.get('title') || '';
  var urlEl = document.getElementById('url');
  var qrEl = document.getElementById('qr');
  if (!/^https?:\/\//i.test(url)) {
    urlEl.textContent = 'Thiếu địa chỉ tờ khai. Hãy mở trang này từ Mã QR & kết nối.';
  } else {
    qrEl.appendChild(window.qrSvg(url, 'Q'));
    urlEl.textContent = url.replace(/\?kiosk=1$/, '');
  }
  if (/kiosk/i.test(title)) document.getElementById('note').textContent = 'Máy tính bảng tự xóa màn hình sau mỗi lượt khai để bảo mật thông tin.';
  document.getElementById('printBtn').addEventListener('click', function () { window.print(); });
  fetch('/api/staff/bootstrap', { credentials: 'same-origin' }).then(function (r) { return r.json(); }).then(function (b) {
    if (b.hospitalName) document.getElementById('hospital').textContent = b.hospitalName;
    if (b.departmentName) document.getElementById('dept').textContent = b.departmentName;
    document.title = 'Áp phích tờ khai · ' + (b.hospitalName || '');
  }).catch(function () { /* keep defaults */ });
})();
