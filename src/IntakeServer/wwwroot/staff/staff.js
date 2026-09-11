/* UMC2 — Trang điều dưỡng: duyệt tờ khai, sinh hiệu, chuyển nội dung sang bác sĩ (HIS Assistant). Vanilla JS, CSP-safe. */
(function () {
  'use strict';

  var C = window.IntakeCompose;
  var root = document.getElementById('root');
  var toasts = document.getElementById('toasts');

  var boot = null;
  var user = null;
  var tab = 'pending';
  var query = '';
  var items = [];
  var counts = {};
  var changeStamp = null;
  var selectedId = null;
  var detail = null;
  var draft = null;
  var composed = null;
  var dirty = false;
  var online = true;
  var pollTimer = null;
  var lastPending = null;
  var soundOn = readPref('umc2-sound', '1') === '1';
  var els = {};
  var aiState = { id: null, draft: null, answers: [], busy: false, question: '' }; // AI suggestions for the record on screen (RAM only)

  var TABS = [
    { id: 'pending', label: 'Chờ duyệt', countKeys: ['pending'] },
    { id: 'approved', label: 'Chờ bác sĩ', countKeys: ['approved', 'claimed'] },
    { id: 'completed', label: 'Đã nhập HIS', countKeys: ['completed'] },
    { id: 'rejected', label: 'Từ chối', countKeys: ['rejected'] }
  ];
  var STATUS_LABEL = { pending: 'Chờ duyệt', approved: 'Chờ bác sĩ', claimed: 'Bác sĩ đang điền', completed: 'Đã nhập HIS', rejected: 'Đã từ chối' };
  var GENDER = { male: 'Nam', female: 'Nữ', other: 'Khác' };
  var ANSWER_LABELS = [
    ['chiefComplaint', 'Lý do khám'], ['onset', 'Khởi phát'], ['symptomDescription', 'Mô tả triệu chứng'], ['painScore', 'Mức độ đau'],
    ['associatedSymptoms', 'Triệu chứng kèm'], ['associatedOther', 'Triệu chứng khác'], ['priorTreatment', 'Điều trị trước đó'],
    ['priorTreatmentDetail', 'Chi tiết điều trị'], ['chronicConditions', 'Bệnh đã/đang mắc'], ['chronicOther', 'Bệnh khác'],
    ['surgeries', 'Phẫu thuật, thủ thuật'], ['medications', 'Thuốc đang dùng'], ['smoking', 'Hút thuốc lá'], ['alcohol', 'Rượu bia'],
    ['pregnancy', 'Mang thai'], ['lastMenstrualPeriod', 'Kinh chót'], ['allergyStatus', 'Dị ứng'], ['allergyDrugs', 'Dị ứng thuốc'],
    ['allergyFoods', 'Dị ứng thức ăn'], ['allergyOther', 'Dị ứng khác'], ['allergyReaction', 'Biểu hiện dị ứng'],
    ['familyConditions', 'Tiền sử gia đình'], ['familyOther', 'Gia đình – bệnh khác'], ['weightKg', 'Cân nặng (kg)'],
    ['heightCm', 'Chiều cao (cm)'], ['notes', 'Ghi chú cho bác sĩ']
  ];
  var ACTION_LABEL = {
    submitted: 'Người bệnh gửi tờ khai', edited: 'Lưu nháp', approved: 'Duyệt, chuyển bác sĩ', rejected: 'Từ chối', reopened: 'Mở lại',
    claimed: 'Máy bác sĩ nhận', released: 'Máy bác sĩ trả lại', completed: 'Đã nhập HIS', 'agent-needs_human': 'Bác sĩ cần xử lý thủ công',
    'agent-failed': 'Điền HIS lỗi', 'claim-expired': 'Hết hạn giữ, trả về hàng chờ'
  };

  // ------------------------------------------------------------------ utilities
  function readPref(key, fallback) { try { var v = localStorage.getItem(key); return v === null ? fallback : v; } catch (e) { return fallback; } }
  function writePref(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* ignore */ } }

  function h(tag, attrs) {
    var el = document.createElement(tag);
    if (attrs) {
      Object.keys(attrs).forEach(function (k) {
        var v = attrs[k];
        if (v === null || v === undefined || v === false) return;
        if (k === 'class') el.className = v;
        else if (k === 'text') el.textContent = v;
        else if (k.slice(0, 2) === 'on') el.addEventListener(k.slice(2), v);
        else if (v === true) el.setAttribute(k, '');
        else el.setAttribute(k, v);
      });
    }
    for (var i = 2; i < arguments.length; i++) add(el, arguments[i]);
    return el;
  }
  function add(el, child) {
    if (child === null || child === undefined || child === false) return;
    if (Array.isArray(child)) { child.forEach(function (c) { add(el, c); }); return; }
    el.appendChild(typeof child === 'string' || typeof child === 'number' ? document.createTextNode(String(child)) : child);
  }
  function clear(el) { while (el.firstChild) el.removeChild(el.firstChild); }

  function toast(message, kind) {
    var t = h('div', { class: 'toast' + (kind ? ' ' + kind : ''), text: message });
    toasts.appendChild(t);
    setTimeout(function () { t.remove(); }, kind === 'error' ? 7000 : 3500);
  }

  function api(method, path, body) {
    var opts = { method: method, credentials: 'same-origin', headers: { Accept: 'application/json' } };
    if (method !== 'GET') {
      opts.headers['X-UMC2'] = '1';
      opts.headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body || {});
    }
    return fetch(path, opts).then(function (res) {
      return res.json().catch(function () { return {}; }).then(function (data) {
        if (res.ok) return data;
        var err = new Error(data.message || ('Lỗi máy chủ (' + res.status + ')'));
        err.status = res.status;
        err.code = data.error;
        throw err;
      });
    });
  }

  function handleError(err) {
    if (err && err.status === 401) { stopPolling(); user = null; renderLogin(err.message); return; }
    if (err && err.code === 'must_change_password') { renderChangePassword(true); return; }
    toast(err && err.message ? err.message : 'Không kết nối được máy chủ.', 'error');
  }

  function parseTime(s) { var d = s ? new Date(s) : null; return d && !isNaN(d.getTime()) ? d : null; }
  function two(n) { return (n < 10 ? '0' : '') + n; }
  function clock(s) { var d = parseTime(s); return d ? two(d.getHours()) + ':' + two(d.getMinutes()) : ''; }
  function dateTime(s) {
    var d = parseTime(s);
    return d ? two(d.getHours()) + ':' + two(d.getMinutes()) + ' ' + two(d.getDate()) + '/' + two(d.getMonth() + 1) : '';
  }
  function ago(s) {
    var d = parseTime(s);
    if (!d) return '';
    var sec = Math.max(0, (Date.now() - d.getTime()) / 1000);
    if (sec < 60) return 'vừa xong';
    if (sec < 3600) return Math.floor(sec / 60) + ' phút trước';
    if (sec < 86400) return Math.floor(sec / 3600) + ' giờ trước';
    return dateTime(s);
  }
  function ageText(year) { return year ? (new Date().getFullYear() - year) + ' tuổi' : ''; }
  function answerText(a, key) {
    if (key === 'onset') return a.onsetValue !== undefined && a.onsetValue !== '' ? a.onsetValue + ' ' + (a.onsetUnit || '') + ' trước' : '';
    var v = a[key];
    if (v === undefined || v === null || v === '') return '';
    if (Array.isArray(v)) return v.join(', ');
    if (key === 'painScore') return v + '/10';
    if (key === 'lastMenstrualPeriod') return String(v).split('-').reverse().join('/');
    return String(v);
  }
  function statusBadge(status) { return h('span', { class: 'badge ' + status, text: STATUS_LABEL[status] || status }); }

  function beep() {
    if (!soundOn) return;
    try {
      var Ctx = window.AudioContext || window.webkitAudioContext;
      var ctx = new Ctx();
      var o = ctx.createOscillator();
      var g = ctx.createGain();
      o.type = 'sine';
      o.frequency.value = 880;
      g.gain.setValueAtTime(0.0001, ctx.currentTime);
      g.gain.exponentialRampToValueAtTime(0.18, ctx.currentTime + 0.02);
      g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.35);
      o.connect(g);
      g.connect(ctx.destination);
      o.start();
      o.stop(ctx.currentTime + 0.4);
      setTimeout(function () { ctx.close(); }, 600);
    } catch (e) { /* audio unavailable */ }
  }

  function modal(title, body, actions, wide) {
    var backdrop = h('div', { class: 'modal-backdrop' });
    var box = h('div', { class: 'modal' + (wide ? ' wide' : ''), role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'mdlTitle' });
    var close = function () { backdrop.remove(); document.removeEventListener('keydown', onKey); };
    var onKey = function (e) { if (e.key === 'Escape') close(); };
    box.appendChild(h('h2', { id: 'mdlTitle', text: title }));
    add(box, body);
    if (actions && actions.length) {
      var bar = h('div', { class: 'modal-actions' });
      actions.forEach(function (a) {
        var b = h('button', { type: 'button', class: 'btn ' + (a.kind || ''), text: a.label });
        b.addEventListener('click', function () {
          var result = a.onClick ? a.onClick(close) : undefined;
          if (result !== false && !a.keepOpen) close();
        });
        bar.appendChild(b);
      });
      box.appendChild(bar);
    }
    backdrop.appendChild(box);
    backdrop.addEventListener('mousedown', function (e) { if (e.target === backdrop) close(); });
    document.addEventListener('keydown', onKey);
    document.body.appendChild(backdrop);
    var focusable = box.querySelector('input, textarea, select, .btn.primary, .btn.ok, .btn');
    if (focusable) focusable.focus();
    return { close: close, box: box };
  }

  function confirmDialog(title, text, okLabel, onOk, kind) {
    modal(title, h('p', { text: text }), [
      { label: 'Hủy' },
      { label: okLabel, kind: kind || 'primary', onClick: onOk }
    ]);
  }

  function field(label, input, hint, id) {
    return h('div', { class: 'field' }, h('label', { for: id || null, text: label }), input, hint ? h('div', { class: 'hint', text: hint }) : null);
  }

  // ------------------------------------------------------------------ auth screens
  function authShell(title, subtitle, content) {
    clear(root);
    root.appendChild(h('div', { class: 'st-center' },
      h('div', { class: 'st-auth' },
        h('div', { class: 'card' },
          h('div', { class: 'st-auth-brand' }, h('img', { src: '/assets/favicon.svg', alt: '' }),
            h('div', null, h('div', { class: 'muted', text: boot ? boot.hospitalName : '' }), h('h1', { text: title }))),
          subtitle ? h('p', { class: 'muted', text: subtitle }) : null,
          content))));
  }

  function renderSetup() {
    if (!boot.isLocalMachine) {
      authShell('Máy chủ chưa được thiết lập', null, h('div', { class: 'alert warn' }, h('div', null,
        h('strong', { text: 'Cần thiết lập trên chính máy chủ' }),
        'Mở trình duyệt trên máy cài UMC2 Intake Server và vào địa chỉ http://localhost:' + location.port + '/ để tạo tài khoản quản trị đầu tiên.')));
      return;
    }
    var hosp = h('input', { id: 'su-h', type: 'text', value: boot.hospitalName === 'Bệnh viện' ? '' : boot.hospitalName, placeholder: 'Ví dụ: Bệnh viện Đại học Y Dược TP.HCM – Cơ sở 2' });
    var name = h('input', { id: 'su-n', type: 'text', placeholder: 'Ví dụ: ĐD. Nguyễn Thị Lan' });
    var usern = h('input', { id: 'su-u', type: 'text', value: 'admin', autocomplete: 'username' });
    var pass = h('input', { id: 'su-p', type: 'password', autocomplete: 'new-password' });
    var pass2 = h('input', { id: 'su-p2', type: 'password', autocomplete: 'new-password' });
    var btn = h('button', { type: 'submit', class: 'btn primary block large', text: 'Tạo tài khoản quản trị' });
    var form = h('form', { class: 'stack' },
      field('Tên bệnh viện / cơ sở', hosp, 'Hiển thị trên tờ khai của người bệnh.', 'su-h'),
      field('Tên hiển thị của bạn', name, null, 'su-n'),
      field('Tên đăng nhập', usern, 'Chữ thường không dấu, số, dấu chấm.', 'su-u'),
      field('Mật khẩu', pass, 'Tối thiểu 8 ký tự, có chữ và số.', 'su-p'),
      field('Nhập lại mật khẩu', pass2, null, 'su-p2'),
      btn);
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      if (pass.value !== pass2.value) { toast('Hai mật khẩu chưa khớp.', 'error'); return; }
      btn.disabled = true;
      api('POST', '/api/setup', { hospitalName: hosp.value, displayName: name.value, username: usern.value, password: pass.value })
        .then(function (r) { user = r.user; return loadBoot(); })
        .then(function () { toast('Đã tạo tài khoản quản trị.', 'ok'); renderMain(); openAdmin('access'); })
        .catch(function (err) { btn.disabled = false; handleError(err); });
    });
    authShell('Thiết lập lần đầu', 'Tạo tài khoản quản trị để quản lý điều dưỡng, máy bác sĩ và kết nối.', form);
    hosp.focus();
  }

  function renderLogin(message) {
    var usern = h('input', { id: 'li-u', type: 'text', autocomplete: 'username', autocapitalize: 'none', spellcheck: 'false' });
    var pass = h('input', { id: 'li-p', type: 'password', autocomplete: 'current-password' });
    var btn = h('button', { type: 'submit', class: 'btn primary block large', text: 'Đăng nhập' });
    var form = h('form', { class: 'stack' },
      message ? h('div', { class: 'alert info', text: message }) : null,
      field('Tên đăng nhập', usern, null, 'li-u'),
      field('Mật khẩu', pass, null, 'li-p'),
      btn,
      h('p', { class: 'hint', text: 'Quên mật khẩu? Nhờ quản trị viên đặt lại trong mục Quản trị → Tài khoản.' }));
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      btn.disabled = true;
      api('POST', '/api/staff/login', { username: usern.value.trim(), password: pass.value })
        .then(function (r) {
          user = r.user;
          if (user.mustChangePassword) renderChangePassword(true);
          else renderMain();
        })
        .catch(function (err) { btn.disabled = false; pass.value = ''; pass.focus(); toast(err.message, 'error'); });
    });
    authShell('Duyệt tờ khai trước khám', 'Đăng nhập bằng tài khoản điều dưỡng.', form);
    usern.focus();
  }

  function renderChangePassword(forced) {
    var cur = h('input', { id: 'cp-c', type: 'password', autocomplete: 'current-password' });
    var next = h('input', { id: 'cp-n', type: 'password', autocomplete: 'new-password' });
    var next2 = h('input', { id: 'cp-n2', type: 'password', autocomplete: 'new-password' });
    var btn = h('button', { type: 'submit', class: 'btn primary block large', text: 'Đổi mật khẩu' });
    var form = h('form', { class: 'stack' },
      forced ? h('div', { class: 'alert warn', text: 'Bạn đang dùng mật khẩu tạm. Hãy đặt mật khẩu riêng để tiếp tục.' }) : null,
      field('Mật khẩu hiện tại', cur, null, 'cp-c'),
      field('Mật khẩu mới', next, 'Tối thiểu 8 ký tự, có chữ và số.', 'cp-n'),
      field('Nhập lại mật khẩu mới', next2, null, 'cp-n2'),
      btn,
      forced ? null : h('button', { type: 'button', class: 'btn ghost block', text: 'Quay lại', onclick: renderMain }));
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      if (next.value !== next2.value) { toast('Hai mật khẩu mới chưa khớp.', 'error'); return; }
      btn.disabled = true;
      api('POST', '/api/staff/password', { currentPassword: cur.value, newPassword: next.value })
        .then(function (r) { user = r.user; user.mustChangePassword = false; toast('Đã đổi mật khẩu.', 'ok'); renderMain(); })
        .catch(function (err) { btn.disabled = false; toast(err.message, 'error'); });
    });
    stopPolling();
    authShell('Đổi mật khẩu', null, form);
    cur.focus();
  }

  function logout() {
    var go = function () {
      api('POST', '/api/staff/logout', {}).catch(function () { /* ignore */ }).then(function () {
        stopPolling();
        user = null;
        detail = null;
        selectedId = null;
        dirty = false;
        renderLogin('Đã đăng xuất.');
      });
    };
    if (dirty) confirmDialog('Đăng xuất?', 'Thay đổi chưa lưu sẽ mất.', 'Đăng xuất', go, 'danger');
    else go();
  }

  // ------------------------------------------------------------------ main layout
  function renderMain() {
    clear(root);
    var soundBtn = h('button', { type: 'button', class: 'btn ghost small', title: 'Âm báo khi có tờ khai mới', text: soundOn ? '🔔 Âm báo' : '🔕 Tắt âm' });
    soundBtn.addEventListener('click', function () {
      soundOn = !soundOn;
      writePref('umc2-sound', soundOn ? '1' : '0');
      soundBtn.textContent = soundOn ? '🔔 Âm báo' : '🔕 Tắt âm';
      if (soundOn) beep();
    });
    els.live = h('span', { class: 'st-live', text: 'Trực tuyến' });
    var top = h('header', { class: 'st-top' },
      h('div', { class: 'st-brand' }, h('img', { src: '/assets/favicon.svg', alt: '' }),
        h('div', { class: 'st-brand-text' },
          h('div', { class: 'st-brand-hospital', text: boot.hospitalName + (boot.departmentName ? ' · ' + boot.departmentName : '') }),
          h('div', { class: 'st-brand-title', text: 'Duyệt tờ khai trước khám' }))),
      h('div', { class: 'spacer' }),
      els.live,
      soundBtn,
      h('button', { type: 'button', class: 'btn small', text: 'Thống kê', onclick: function () { renderDashboard(); } }),
      h('button', { type: 'button', class: 'btn small', text: 'Xuất Excel', title: 'Tải danh sách tờ khai 30 ngày (.xlsx)', onclick: function () { exportExcel(30, ''); } }),
      h('button', { type: 'button', class: 'btn small', text: 'Mã QR & kết nối', onclick: function () { openAdmin('access'); } }),
      user.role === 'admin' ? h('button', { type: 'button', class: 'btn small', text: 'Quản trị', onclick: function () { openAdmin('devices'); } }) : null,
      h('div', { class: 'st-user' },
        h('span', { class: 'name', text: user.displayName }),
        h('button', { type: 'button', class: 'btn ghost small', text: 'Đổi mật khẩu', onclick: function () { renderChangePassword(false); } }),
        h('button', { type: 'button', class: 'btn ghost small', text: 'Đăng xuất', onclick: logout })));

    els.tabs = h('div', { class: 'st-tabs', role: 'tablist' });
    els.search = h('input', { type: 'search', placeholder: 'Tìm mã tờ khai, tên, SĐT, mã BN…  ( / )', 'aria-label': 'Tìm kiếm', value: query });
    var searchTimer = null;
    els.search.addEventListener('input', function () {
      clearTimeout(searchTimer);
      searchTimer = setTimeout(function () { query = els.search.value.trim(); refreshList(true); }, 250);
    });
    els.list = h('ul', { class: 'st-list', 'aria-label': 'Danh sách tờ khai' });
    var side = h('aside', { class: 'st-side' }, els.tabs, h('div', { class: 'st-search' }, els.search), els.list);
    els.main = h('main', { class: 'st-main' });
    root.appendChild(top);
    root.appendChild(h('div', { class: 'st-layout' }, side, els.main));
    renderTabs();
    renderPlaceholder();
    refreshList(true);
    startPolling();
  }

  function renderTabs() {
    clear(els.tabs);
    TABS.forEach(function (t) {
      var n = t.countKeys.reduce(function (sum, k) { return sum + (counts[k] || 0); }, 0);
      var b = h('button', { type: 'button', class: 'st-tab' + (t.id === 'pending' && n > 0 ? ' alert-dot' : ''), role: 'tab',
        'aria-selected': tab === t.id ? 'true' : 'false' }, h('span', { class: 'count', text: String(n) }), h('span', { text: t.label }));
      b.addEventListener('click', function () {
        if (tab === t.id) return;
        tab = t.id;
        renderTabs();
        refreshList(true);
      });
      els.tabs.appendChild(b);
    });
  }

  function renderList() {
    clear(els.list);
    if (!items.length) {
      els.list.appendChild(h('li', { class: 'st-empty', text: query ? 'Không có kết quả phù hợp.' : (tab === 'pending' ? 'Chưa có tờ khai nào chờ duyệt.' : 'Danh sách trống.') }));
      return;
    }
    items.forEach(function (it) {
      var meta = h('div', { class: 'st-item-meta' }, h('span', { class: 'st-code', text: it.code }));
      if (tab === 'approved') meta.appendChild(statusBadge(it.status));
      if (it.source === 'internet') meta.appendChild(h('span', { class: 'badge internet', text: 'Internet' }));
      if (it.hisPatientId) meta.appendChild(h('span', { class: 'badge', text: 'BN ' + it.hisPatientId }));
      if (it.status === 'claimed' && it.deviceName) meta.appendChild(h('span', { class: 'badge claimed', text: it.deviceName }));
      var b = h('button', { type: 'button', class: 'st-item', 'aria-current': it.id === selectedId ? 'true' : 'false' },
        h('div', { class: 'st-item-top' },
          h('span', { class: 'st-item-name', text: it.fullName }),
          h('span', { class: 'st-item-time', text: ago(tab === 'pending' ? it.createdAt : it.updatedAt) })),
        h('div', { class: 'st-item-sub', text: [GENDER[it.gender], ageText(it.birthYear), it.phoneMasked].filter(Boolean).join(' · ') + ' — ' + (it.chiefComplaint || '') }),
        meta);
      b.addEventListener('click', function () { select(it.id); });
      els.list.appendChild(h('li', null, b));
    });
  }

  function refreshList(force) {
    var url = '/api/staff/intakes?status=' + encodeURIComponent(tab) + (query ? '&q=' + encodeURIComponent(query) : '');
    return api('GET', url).then(function (r) {
      setOnline(true);
      var pending = r.counts ? r.counts.pending || 0 : 0;
      if (lastPending !== null && pending > lastPending) {
        beep();
        toast('Có ' + (pending - lastPending) + ' tờ khai mới chờ duyệt.');
      }
      lastPending = pending;
      document.title = (pending ? '(' + pending + ') ' : '') + 'Duyệt tờ khai · ' + boot.hospitalName;
      var changed = force || r.changeStamp !== changeStamp;
      changeStamp = r.changeStamp;
      counts = r.counts || {};
      items = r.items || [];
      renderTabs();
      if (changed) renderList();
      if (changed && detail && !dirty) {
        var fresh = items.filter(function (i) { return i.id === detail.id; })[0];
        if (fresh && fresh.updatedAt !== detail.updatedAt) loadDetail(detail.id, true);
      }
    }).catch(function (err) {
      if (err && err.status) handleError(err);
      else setOnline(false);
    });
  }

  function setOnline(value) {
    if (online === value || !els.live) return;
    online = value;
    els.live.className = 'st-live' + (value ? '' : ' offline');
    els.live.textContent = value ? 'Trực tuyến' : 'Mất kết nối máy chủ';
    if (!value) toast('Mất kết nối máy chủ tờ khai. Đang thử lại…', 'error');
  }

  function startPolling() {
    stopPolling();
    pollTimer = setInterval(function () { if (!document.hidden) refreshList(false); }, 5000);
  }
  function stopPolling() { if (pollTimer) clearInterval(pollTimer); pollTimer = null; }

  // ------------------------------------------------------------------ detail
  function renderPlaceholder() {
    clear(els.main);
    els.main.appendChild(h('div', { class: 'st-placeholder' },
      h('div', { class: 'big', 'aria-hidden': 'true', text: '🗂️' }),
      h('h2', { text: 'Chọn một tờ khai để xem và duyệt' }),
      h('p', { text: 'Người bệnh đưa mã tờ khai (ví dụ K7P-29Q): gõ mã vào ô tìm kiếm để mở nhanh.' }),
      h('p', { class: 'hint' }, 'Phím tắt: ', h('span', { class: 'st-kbd', text: '/' }), ' tìm kiếm · ',
        h('span', { class: 'st-kbd', text: 'Ctrl+Enter' }), ' duyệt · ', h('span', { class: 'st-kbd', text: 'Ctrl+S' }), ' lưu nháp')));
  }

  function select(id) {
    if (id === selectedId) return;
    var go = function () { dirty = false; selectedId = id; renderList(); loadDetail(id, false); };
    if (dirty) confirmDialog('Bỏ thay đổi chưa lưu?', 'Tờ khai đang mở có nội dung chưa lưu.', 'Bỏ thay đổi', go, 'danger');
    else go();
  }

  function loadDetail(id, silent) {
    if (!silent) {
      clear(els.main);
      els.main.appendChild(h('div', { class: 'st-placeholder' }, h('span', { class: 'spinner' })));
    }
    return api('GET', '/api/staff/intakes/' + id).then(function (r) {
      detail = r;
      selectedId = r.id;
      composed = C.composeHisFields(r);
      var saved = r.review && r.review.fields ? r.review.fields : {};
      var fields = {};
      if (Object.keys(saved).length) Object.keys(saved).forEach(function (k) { fields[k] = saved[k]; });
      else Object.keys(composed).forEach(function (k) { if (composed[k]) fields[k] = composed[k]; });
      draft = { hisPatientId: r.review.hisPatientId || '', fields: fields, note: r.review.note || '' };
      dirty = false;
      renderDetail();
    }).catch(function (err) {
      if (err.status === 404) { toast(err.message, 'error'); detail = null; selectedId = null; renderPlaceholder(); refreshList(true); return; }
      handleError(err);
    });
  }

  function editable() { return detail && (detail.status === 'pending' || detail.status === 'approved'); }
  function markDirty() { if (!dirty) { dirty = true; if (els.dirtyNote) els.dirtyNote.textContent = 'Có thay đổi chưa lưu'; } }

  function renderDetail() {
    clear(els.main);
    var r = detail;
    var p = r.patient;
    var a = r.answers || {};
    var canEdit = editable();

    var flags = h('div', { class: 'row tight' }, statusBadge(r.status));
    if (r.source === 'internet') flags.appendChild(h('span', { class: 'badge internet', text: 'Khai qua Internet' }));
    if (p.filledBy === 'relative') flags.appendChild(h('span', { class: 'badge', text: 'Người nhà khai' + (p.relation ? ' (' + p.relation + ')' : '') }));
    if (a.allergyStatus === 'Có') flags.appendChild(h('span', { class: 'badge rejected', text: 'Dị ứng: ' + ([a.allergyDrugs, a.allergyFoods, a.allergyOther].filter(Boolean).join(', ') || 'có') }));
    if (a.pregnancy === 'Có') flags.appendChild(h('span', { class: 'badge pending', text: 'Đang mang thai' }));
    if (Number(a.painScore) >= 8) flags.appendChild(h('span', { class: 'badge pending', text: 'Đau nhiều ' + a.painScore + '/10' }));

    var head = h('div', { class: 'st-head' },
      h('div', null,
        h('h1', { text: p.fullName }),
        h('div', { class: 'sub', text: [GENDER[p.gender], p.birthDate ? (p.birthDate.length === 4 ? 'sinh năm ' + p.birthDate : 'sinh ' + p.birthDate.split('-').reverse().join('/')) : '', ageText(p.birthYear)].filter(Boolean).join(' · ') }),
        h('div', { class: 'row tight' }, flags)),
      h('div', { class: 'stack' },
        h('div', { class: 'row tight end' }, h('span', { class: 'muted', text: 'Mã tờ khai' }), h('span', { class: 'st-code', text: r.code })),
        h('div', { class: 'hint', text: 'Gửi lúc ' + dateTime(r.createdAt) + ' (' + ago(r.createdAt) + ')' })));

    var wrap = h('div', { class: 'st-detail' }, head);
    var sections = h('div', { class: 'st-sections' });
    wrap.appendChild(sections);

    var panel = statusPanel(r);
    if (panel) sections.appendChild(panel);
    sections.appendChild(identityCard(r, canEdit));
    sections.appendChild(vitalsCard(canEdit));
    sections.appendChild(textCard(canEdit));
    sections.appendChild(aiCard(r, canEdit));
    sections.appendChild(rawAnswersCard(r));
    sections.appendChild(noteCard(canEdit));
    sections.appendChild(historyCard(r));
    wrap.appendChild(actionBar(r, canEdit));
    els.main.appendChild(wrap);
    els.main.scrollTop = 0;
    if (canEdit && r.status === 'pending' && els.hisId && !draft.hisPatientId) els.hisId.focus();
  }

  function statusPanel(r) {
    var text, kind = 'info';
    if (r.status === 'approved') text = 'Đã duyệt bởi ' + r.review.approvedBy + ' lúc ' + dateTime(r.review.approvedAt) + '. Máy bác sĩ sẽ tự hiện tờ khai khi mở hồ sơ BN' + (r.review.hisPatientId ? ' mã ' + r.review.hisPatientId : '') + ' trên HIS. Vẫn có thể sửa và duyệt lại trước khi bác sĩ nhận.';
    else if (r.status === 'claimed') { text = 'Máy ' + r.agent.deviceName + ' đang điền vào HIS (nhận lúc ' + dateTime(r.agent.claimedAt) + '). Không sửa được lúc này.'; }
    else if (r.status === 'completed') { kind = 'ok'; text = 'Đã nhập HIS lúc ' + dateTime(r.agent.completedAt) + ' từ máy ' + r.agent.deviceName + (r.agent.filledCount ? ' (' + r.agent.filledCount + ' trường)' : '') + (r.agent.summary ? '. ' + r.agent.summary : '') + '.'; }
    else if (r.status === 'rejected') { kind = 'danger'; text = 'Đã từ chối bởi ' + r.review.rejectedBy + ' lúc ' + dateTime(r.review.rejectedAt) + ': ' + r.review.rejectReason; }
    else if (r.agent && r.agent.result === 'needs_human') { kind = 'warn'; text = 'Máy bác sĩ báo cần xử lý thủ công: ' + (r.agent.summary || '') ; }
    if (!text) return null;
    return h('div', { class: 'alert ' + kind }, h('div', null, text));
  }

  function identityCard(r, canEdit) {
    var p = r.patient;
    var dl = h('dl', { class: 'kv' });
    [['Họ tên', p.fullName], ['Ngày sinh', p.birthDate ? p.birthDate.split('-').reverse().join('/') : ''], ['Điện thoại', p.phone],
      ['CCCD', p.nationalId], ['Mã BN do BN khai', p.hisPatientId]].forEach(function (row) {
      if (!row[1]) return;
      dl.appendChild(h('dt', { text: row[0] }));
      dl.appendChild(h('dd', { text: row[1] }));
    });
    els.hisId = h('input', { id: 'hisId', type: 'text', value: draft.hisPatientId, placeholder: 'Quét mã vạch hoặc gõ mã BN', maxlength: 30,
      autocomplete: 'off', spellcheck: 'false', readonly: canEdit ? null : true });
    els.hisId.addEventListener('input', function () { draft.hisPatientId = els.hisId.value.trim(); markDirty(); });
    els.hisId.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); var t = document.querySelector('[data-vital="Pulse"]'); if (t) t.focus(); } });
    var useSelf = p.hisPatientId && canEdit && !draft.hisPatientId
      ? h('button', { type: 'button', class: 'btn small', text: 'Dùng mã BN tự khai: ' + p.hisPatientId, onclick: function () {
        els.hisId.value = p.hisPatientId; draft.hisPatientId = p.hisPatientId; markDirty(); useSelf.remove();
      } })
      : null;
    var idBox = h('div', { class: 'st-hisid stack' },
      h('div', { class: 'field' }, h('label', { for: 'hisId', class: 'section-label', text: 'Mã bệnh nhân trên HIS' }), els.hisId,
        h('div', { class: 'hint', text: 'Lấy từ phiếu khám/HIS sau khi đối chiếu CCCD hoặc SĐT. Bác sĩ mở đúng hồ sơ này trên HIS thì tờ khai tự hiện.' })),
      useSelf);
    return h('section', { class: 'card' },
      h('div', { class: 'card-title' }, h('h3', { text: 'Đối chiếu danh tính & liên kết hồ sơ HIS' })),
      h('div', { class: 'st-idbox' }, dl, idBox));
  }

  function vitalsCard(canEdit) {
    var grid = h('div', { class: 'st-vitals' });
    var bmiEl = h('div', { class: 'st-bmi' });
    function updateBmi() {
      var v = C.bmi(draft.fields.Weight, draft.fields.Height);
      bmiEl.textContent = v ? 'BMI ≈ ' + v : '';
    }
    C.VITAL_FIELDS.forEach(function (f) {
      var id = 'v-' + f.key;
      var input = h('input', { id: id, type: 'text', inputmode: f.key === 'BloodPressure' ? 'text' : 'decimal', value: draft.fields[f.key] || '',
        placeholder: f.placeholder, 'data-vital': f.key, readonly: canEdit ? null : true, autocomplete: 'off' });
      var err = h('div', { class: 'error-text' });
      input.addEventListener('input', function () {
        var v = input.value.trim();
        if (v) draft.fields[f.key] = v; else delete draft.fields[f.key];
        markDirty();
        if (f.key === 'Weight' || f.key === 'Height') updateBmi();
      });
      input.addEventListener('blur', function () {
        var msg = C.validateVital(f, input.value);
        err.textContent = msg || '';
        input.setAttribute('aria-invalid', msg ? 'true' : 'false');
      });
      grid.appendChild(h('div', { class: 'field' }, h('label', { for: id, text: f.label + ' (' + f.unit + ')' }), input, err));
    });
    updateBmi();
    return h('section', { class: 'card' },
      h('div', { class: 'card-title' }, h('h3', { text: 'Sinh hiệu điều dưỡng đo' }),
        h('span', { class: 'hint', text: 'Cân nặng/chiều cao lấy sẵn từ BN khai, sửa nếu đo lại.' })),
      grid, bmiEl);
  }

  function textCard(canEdit) {
    var list = h('div', { class: 'st-textfields' });
    C.TEXT_FIELDS.forEach(function (f) {
      var id = 't-' + f.key;
      var marker = h('span', { class: 'st-changed' });
      var ta = h('textarea', { id: id, rows: f.rows, readonly: canEdit ? null : true });
      ta.value = draft.fields[f.key] || '';
      var updateMarker = function () { marker.textContent = (composed[f.key] || '') !== (draft.fields[f.key] || '') ? 'đã chỉnh sửa' : ''; };
      ta.addEventListener('input', function () {
        if (ta.value.trim()) draft.fields[f.key] = ta.value; else delete draft.fields[f.key];
        markDirty();
        updateMarker();
      });
      updateMarker();
      list.appendChild(h('div', { class: 'field' }, h('div', { class: 'st-field-head' }, h('label', { for: id, text: f.label }), marker), ta));
    });
    var recompose = canEdit ? h('button', { type: 'button', class: 'btn small', text: 'Soạn lại từ tờ khai' }) : null;
    if (recompose) recompose.addEventListener('click', function () {
      var apply = function () {
        C.TEXT_FIELDS.forEach(function (f) { if (composed[f.key]) draft.fields[f.key] = composed[f.key]; else delete draft.fields[f.key]; });
        markDirty();
        renderDetail();
      };
      confirmDialog('Soạn lại nội dung?', 'Các ô văn bản sẽ được thay bằng bản soạn tự động từ câu trả lời của người bệnh (sinh hiệu giữ nguyên).', 'Soạn lại', apply);
    });
    return h('section', { class: 'card' },
      h('div', { class: 'card-title' }, h('h3', { text: 'Nội dung chuyển vào HIS (phần bác sĩ)' }), recompose),
      h('p', { class: 'hint', text: 'Bản nháp soạn tự động từ câu trả lời của người bệnh. Điều dưỡng sửa cho đúng trước khi duyệt; bác sĩ vẫn kiểm tra lại trên HIS trước khi lưu.' }),
      list);
  }

  // ------------------------------------------------------------------ controlled AI
  function aiFeedback(decision, fields) {
    if (!detail) return;
    api('POST', '/api/staff/intakes/' + detail.id + '/ai-feedback', { decision: decision, fields: fields || 0 }).catch(function () { /* audit only */ });
  }

  function aiCard(r, canEdit) {
    if (aiState.id !== r.id) aiState = { id: r.id, draft: null, answers: [], busy: false, question: '' };
    var body = h('div', { class: 'stack' });
    var card = h('section', { class: 'card st-ai' },
      h('div', { class: 'card-title' }, h('h3', { text: 'Trợ lý AI có kiểm soát' }), h('span', { class: 'badge', text: boot.aiEnabled ? 'Đã bật' : 'Chưa cấu hình' })),
      h('p', { class: 'hint', text: 'AI chỉ nhận câu trả lời lâm sàng của tờ khai này (không có họ tên, số điện thoại, mã BN). Mọi gợi ý đều phải được điều dưỡng chấp nhận từng mục, sửa hoặc bỏ; mỗi quyết định được ghi vào nhật ký.' }),
      body);
    if (!boot.aiEnabled) {
      body.appendChild(h('div', { class: 'alert info' }, h('div', null, user.role === 'admin'
        ? 'Chưa cấu hình khóa API. Vào Quản trị → Cài đặt → Trợ lý AI để bật.'
        : 'Quản trị viên chưa bật trợ lý AI. Vẫn dùng được bản soạn theo quy tắc ở khung trên.')));
      return card;
    }
    var draftBtn = h('button', { type: 'button', class: 'btn primary small', text: aiState.draft ? 'Soạn lại bằng AI' : 'Dự thảo nội dung bằng AI', disabled: aiState.busy ? true : null });
    draftBtn.addEventListener('click', function () {
      aiState.busy = true;
      renderDetail();
      api('POST', '/api/staff/intakes/' + r.id + '/ai', { task: 'draft' }).then(function (res) {
        aiState.busy = false;
        aiState.draft = res;
        aiState.accepted = {};
        renderDetail();
      }).catch(function (err) { aiState.busy = false; renderDetail(); handleError(err); });
    });
    var actions = h('div', { class: 'row tight' }, draftBtn);
    if (aiState.busy) actions.appendChild(h('span', { class: 'hint' }, h('span', { class: 'spinner small' }), ' Đang gọi mô hình…'));
    body.appendChild(actions);

    var d = aiState.draft;
    if (d) {
      if ((d.redFlags || []).length) {
        var flagsUl = h('ul', null);
        d.redFlags.forEach(function (f) { flagsUl.appendChild(h('li', { text: f })); });
        body.appendChild(h('div', { class: 'alert warn' }, h('div', null, h('strong', { text: 'Dấu hiệu cần lưu ý (AI gợi ý, cần điều dưỡng/bác sĩ xác nhận): ' }), flagsUl)));
      }
      var list = h('div', { class: 'st-ai-fields' });
      var keys = C.TEXT_FIELDS.map(function (f) { return f.key; }).concat(['PreliminaryDiagnosis']);
      var labels = {};
      C.TEXT_FIELDS.forEach(function (f) { labels[f.key] = f.label; });
      labels.PreliminaryDiagnosis = 'Chẩn đoán sơ bộ gợi ý (bác sĩ quyết định)';
      var usable = 0;
      keys.forEach(function (key) {
        var text = d.fields && d.fields[key];
        if (!text) return;
        usable++;
        var accepted = aiState.accepted && aiState.accepted[key];
        var ta = h('textarea', { rows: Math.min(6, Math.max(2, Math.ceil(text.length / 90))), readonly: true });
        ta.value = text;
        var useBtn = canEdit && key !== 'PreliminaryDiagnosis'
          ? h('button', { type: 'button', class: 'btn small ' + (accepted ? 'ghost' : ''), text: accepted ? 'Đã dùng' : 'Dùng mục này', disabled: accepted ? true : null })
          : null;
        if (useBtn) useBtn.addEventListener('click', function () {
          draft.fields[key] = text;
          aiState.accepted[key] = true;
          markDirty();
          aiFeedback('partial', 1);
          renderDetail();
        });
        list.appendChild(h('div', { class: 'field' }, h('div', { class: 'st-field-head' }, h('label', { text: labels[key] || key }), useBtn), ta));
      });
      body.appendChild(list);
      var meta = h('div', { class: 'row tight wrap' });
      if ((d.icd || []).length) meta.appendChild(h('span', { class: 'hint', text: 'ICD-10 gợi ý: ' + d.icd.join(' · ') }));
      if (d.confidence) meta.appendChild(h('span', { class: 'badge', text: 'Độ tin cậy: ' + d.confidence }));
      if ((d.missing || []).length) meta.appendChild(h('span', { class: 'hint', text: 'Còn thiếu: ' + d.missing.join('; ') }));
      body.appendChild(meta);
      if (canEdit) {
        var useAll = h('button', { type: 'button', class: 'btn small', text: 'Dùng tất cả (vẫn sửa được)' });
        useAll.addEventListener('click', function () {
          var n = 0;
          C.TEXT_FIELDS.forEach(function (f) { if (d.fields[f.key]) { draft.fields[f.key] = d.fields[f.key]; aiState.accepted[f.key] = true; n++; } });
          markDirty();
          aiFeedback('accept', n);
          renderDetail();
          toast('Đã đưa ' + n + ' mục vào bản nháp — hãy đọc và sửa trước khi duyệt.');
        });
        var reject = h('button', { type: 'button', class: 'btn ghost small', text: 'Không phù hợp, bỏ toàn bộ' });
        reject.addEventListener('click', function () { aiFeedback('reject', usable); aiState.draft = null; renderDetail(); toast('Đã bỏ bản nháp AI (đã ghi nhận vào nhật ký).'); });
        body.appendChild(h('div', { class: 'row tight' }, useAll, reject));
      }
      body.appendChild(h('p', { class: 'hint', text: d.disclaimer + ' Mô hình: ' + d.model }));
    }

    // Q&A about this record only
    var q = h('input', { type: 'text', placeholder: 'Hỏi về tờ khai này, ví dụ: "Có dị ứng thuốc nào cần lưu ý?"', value: aiState.question, 'aria-label': 'Câu hỏi cho AI' });
    var askBtn = h('button', { type: 'button', class: 'btn small', text: 'Hỏi' });
    var ask = function () {
      var question = q.value.trim();
      if (question.length < 3) { q.focus(); return; }
      askBtn.disabled = true;
      api('POST', '/api/staff/intakes/' + r.id + '/ai', { task: 'ask', question: question }).then(function (res) {
        aiState.answers.unshift({ q: question, a: res.answer, inScope: res.inScope });
        aiState.question = '';
        renderDetail();
      }).catch(function (err) { askBtn.disabled = false; handleError(err); });
    };
    askBtn.addEventListener('click', ask);
    q.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); ask(); } });
    q.addEventListener('input', function () { aiState.question = q.value; });
    var qa = h('div', { class: 'st-ai-qa' }, h('div', { class: 'row tight' }, q, askBtn));
    aiState.answers.slice(0, 4).forEach(function (item) {
      qa.appendChild(h('div', { class: 'st-ai-answer' + (item.inScope ? '' : ' out') },
        h('div', { class: 'st-ai-q', text: 'Hỏi: ' + item.q }),
        h('div', { class: 'st-ai-a', text: (item.inScope ? '' : '[Ngoài phạm vi] ') + item.a })));
    });
    body.appendChild(qa);
    return card;
  }

  function rawAnswersCard(r) {
    var a = r.answers || {};
    var dl = h('dl', { class: 'kv' });
    ANSWER_LABELS.forEach(function (pair) {
      var v = answerText(a, pair[0]);
      if (!v) return;
      dl.appendChild(h('dt', { text: pair[1] }));
      dl.appendChild(h('dd', { text: v }));
    });
    return h('section', { class: 'card' }, h('details', { class: 'st-raw' },
      h('summary', { text: 'Câu trả lời gốc của người bệnh' }), dl));
  }

  function noteCard(canEdit) {
    var ta = h('textarea', { id: 'note', rows: 2, readonly: canEdit ? null : true, placeholder: 'Không chuyển vào HIS. Ví dụ: đã gọi xác nhận SĐT, BN đi cùng người nhà…' });
    ta.value = draft.note;
    ta.addEventListener('input', function () { draft.note = ta.value; markDirty(); });
    return h('section', { class: 'card' }, field('Ghi chú nội bộ của điều dưỡng', ta, null, 'note'));
  }

  function historyCard(r) {
    var ul = h('ul', { class: 'st-timeline' });
    (r.history || []).slice().reverse().forEach(function (e) {
      ul.appendChild(h('li', null, h('span', { class: 'muted', text: dateTime(e.at) }),
        h('span', null, (ACTION_LABEL[e.action] || e.action) + ' — ' + (e.actor || ''))));
    });
    return h('section', { class: 'card' }, h('details', { class: 'st-raw' }, h('summary', { text: 'Lịch sử thao tác' }), ul));
  }

  function actionBar(r, canEdit) {
    els.dirtyNote = h('span', { class: 'note', text: dirty ? 'Có thay đổi chưa lưu' : '' });
    var bar = h('div', { class: 'st-actionbar' }, els.dirtyNote);
    if (user.role === 'admin') bar.appendChild(h('button', { type: 'button', class: 'btn danger small', text: 'Xóa', onclick: deleteIntake }));
    if (canEdit) {
      bar.appendChild(h('button', { type: 'button', class: 'btn danger', text: 'Từ chối…', onclick: rejectIntake }));
      bar.appendChild(h('button', { type: 'button', class: 'btn', text: 'Lưu nháp', onclick: function () { save('save'); } }));
      bar.appendChild(h('button', { type: 'button', class: 'btn ok large', text: r.status === 'approved' ? 'Lưu & duyệt lại' : 'Duyệt & chuyển bác sĩ', onclick: function () { save('approve'); } }));
    } else {
      bar.appendChild(h('button', { type: 'button', class: 'btn', text: 'Mở lại tờ khai', onclick: reopenIntake }));
    }
    return bar;
  }

  function payload(action) {
    var fields = {};
    Object.keys(draft.fields).forEach(function (k) { var v = String(draft.fields[k] || '').trim(); if (v) fields[k] = v; });
    return { action: action, hisPatientId: (draft.hisPatientId || '').trim(), fields: fields, note: draft.note || '' };
  }

  function save(action) {
    if (!editable()) return;
    var problems = [];
    C.VITAL_FIELDS.forEach(function (f) { var m = C.validateVital(f, draft.fields[f.key]); if (m) problems.push(m); });
    if (problems.length && action === 'approve') { toast(problems[0], 'error'); return; }
    var send = function () {
      api('POST', '/api/staff/intakes/' + detail.id + '/review', payload(action)).then(function (r) {
        dirty = false;
        if (action === 'approve') {
          toast('Đã duyệt ' + r.code + ' — bác sĩ sẽ thấy khi mở hồ sơ trên HIS.', 'ok');
          var next = items.filter(function (i) { return i.id !== r.id && i.status === 'pending'; })[0];
          detail = null;
          refreshList(true).then(function () { if (tab === 'pending' && next) { selectedId = null; select(next.id); } else { selectedId = null; renderPlaceholder(); renderList(); } });
        } else {
          toast('Đã lưu nháp.', 'ok');
          detail = r;
          renderDetail();
          refreshList(true);
        }
      }).catch(handleError);
    };
    if (action === 'approve' && !(draft.hisPatientId || '').trim()) {
      confirmDialog('Duyệt khi chưa có mã BN?',
        'Máy bác sĩ sẽ chỉ ghép được theo họ tên + năm sinh và bác sĩ phải xác nhận thủ công. Nên nhập mã BN trên HIS nếu có.',
        'Vẫn duyệt', send, 'ok');
      return;
    }
    send();
  }

  function rejectIntake() {
    var reason = h('textarea', { id: 'rj', rows: 3, placeholder: 'Lý do từ chối' });
    var chips = h('div', { class: 'chips' });
    ['Trùng tờ khai', 'Không đến khám', 'Thông tin sai hoặc thiếu', 'Khai thử / nhầm'].forEach(function (t) {
      chips.appendChild(h('button', { type: 'button', class: 'chip', text: t, onclick: function () { reason.value = t; } }));
    });
    modal('Từ chối tờ khai ' + detail.code, h('div', { class: 'stack' }, chips, reason,
      h('p', { class: 'hint', text: 'Tờ khai bị từ chối không chuyển cho bác sĩ và tự xóa theo thời hạn lưu trữ.' })), [
      { label: 'Hủy' },
      { label: 'Từ chối', kind: 'danger', onClick: function () {
        if (!reason.value.trim()) { toast('Vui lòng ghi lý do.', 'error'); return false; }
        api('POST', '/api/staff/intakes/' + detail.id + '/reject', { reason: reason.value }).then(function (r) {
          dirty = false; toast('Đã từ chối ' + r.code + '.'); detail = r; renderDetail(); refreshList(true);
        }).catch(handleError);
      } }
    ]);
  }

  function reopenIntake() {
    var warn = detail.status === 'completed' ? 'Tờ khai đã được nhập HIS. Mở lại có thể khiến bác sĩ điền lần nữa — chỉ làm khi thật cần.'
      : detail.status === 'claimed' ? 'Máy bác sĩ đang xử lý tờ khai này. Chỉ mở lại khi đã trao đổi với bác sĩ.'
        : 'Tờ khai sẽ về trạng thái Chờ duyệt.';
    confirmDialog('Mở lại tờ khai?', warn, 'Mở lại', function () {
      api('POST', '/api/staff/intakes/' + detail.id + '/reopen', {}).then(function (r) {
        toast('Đã mở lại ' + r.code + '.');
        tab = 'pending';
        renderTabs();
        detail = null;
        selectedId = null;
        refreshList(true).then(function () { select(r.id); });
      }).catch(handleError);
    });
  }

  function deleteIntake() {
    confirmDialog('Xóa vĩnh viễn?', 'Tờ khai ' + detail.code + ' sẽ bị xóa khỏi máy chủ (không ảnh hưởng hồ sơ trên HIS).', 'Xóa', function () {
      api('DELETE', '/api/staff/intakes/' + detail.id).then(function () {
        toast('Đã xóa.');
        detail = null; selectedId = null; dirty = false;
        renderPlaceholder();
        refreshList(true);
      }).catch(handleError);
    }, 'danger');
  }

  // ------------------------------------------------------------------ admin / access
  // ------------------------------------------------------------------ dashboard / export
  function exportExcel(days, status) {
    // Cookie-authenticated GET download; the server writes an audit entry (who/when/range, no content).
    var a = document.createElement('a');
    a.href = '/api/staff/export.xlsx?days=' + encodeURIComponent(days) + (status ? '&status=' + encodeURIComponent(status) : '');
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    toast('Đang tải file Excel…');
  }

  function tile(value, label, cls) {
    return h('div', { class: 'st-tile' + (cls ? ' ' + cls : '') }, h('div', { class: 'st-tile-value', text: String(value) }), h('div', { class: 'st-tile-label', text: label }));
  }

  function minutesText(m) {
    if (!m) return '—';
    if (m < 60) return Math.round(m) + ' phút';
    if (m < 60 * 24) return (m / 60).toFixed(1) + ' giờ';
    return (m / 60 / 24).toFixed(1) + ' ngày';
  }

  function barChart(rows, key, color) {
    var max = rows.reduce(function (m, r) { return Math.max(m, r[key] || 0); }, 0) || 1;
    var chart = h('div', { class: 'st-bars', role: 'img', 'aria-label': 'Số tờ khai theo ngày' });
    rows.forEach(function (r) {
      var v = r[key] || 0;
      var bar = h('div', { class: 'st-bar ' + color, title: r.label + ': ' + v });
      bar.style.height = Math.max(2, Math.round(v / max * 100)) + '%';
      chart.appendChild(h('div', { class: 'st-bar-col' }, h('div', { class: 'st-bar-value', text: v ? String(v) : '' }), bar, h('div', { class: 'st-bar-label', text: r.label })));
    });
    return chart;
  }

  function renderDashboard() {
    selectedId = null;
    clear(els.main);
    els.main.appendChild(h('div', { class: 'st-placeholder' }, h('span', { class: 'spinner' })));
    var days = Number(readPref('umc2-dash-days', '14')) || 14;
    api('GET', '/api/staff/stats?days=' + days).then(function (s) {
      clear(els.main);
      var c = s.counts || {};
      var w = s.window || {};
      var t = s.today || {};
      var range = h('select', null, [7, 14, 30, 90].map(function (d) { return h('option', { value: String(d), text: d + ' ngày' }); }));
      range.value = String(days);
      range.addEventListener('change', function () { writePref('umc2-dash-days', range.value); renderDashboard(); });
      var head = h('div', { class: 'st-head' },
        h('div', null, h('h1', { text: 'Thống kê hoạt động' }), h('div', { class: 'sub', text: 'Tổng cộng ' + s.total + ' tờ khai đang lưu · cập nhật ' + clock(s.generatedAt) })),
        h('div', { class: 'row tight end' }, range,
          h('button', { type: 'button', class: 'btn small', text: 'Xuất Excel ' + days + ' ngày', onclick: function () { exportExcel(days, ''); } }),
          h('button', { type: 'button', class: 'btn ghost small', text: 'In / PDF', onclick: function () { window.print(); } })));
      var tiles = h('div', { class: 'st-tiles' },
        tile(t.submitted || 0, 'Tờ khai hôm nay'),
        tile(c.pending || 0, 'Đang chờ duyệt', 'pending'),
        tile((c.approved || 0) + (c.claimed || 0), 'Chờ bác sĩ / đang điền', 'approved'),
        tile(w.completed || 0, 'Đã nhập HIS (' + days + ' ngày)', 'completed'),
        tile(minutesText(w.medianApproveMinutes), 'Trung vị: gửi → duyệt'),
        tile(minutesText(w.medianHisMinutes), 'Trung vị: duyệt → vào HIS'),
        tile(w.fieldsFilled || 0, 'Trường đã điền tự động'),
        tile(w.submitted ? Math.round((w.internet || 0) / w.submitted * 100) + '%' : '0%', 'Khai từ Internet'));
      var statusRows = [
        { key: 'pending', label: 'Chờ duyệt' }, { key: 'approved', label: 'Chờ bác sĩ' }, { key: 'claimed', label: 'Bác sĩ đang điền' },
        { key: 'completed', label: 'Đã nhập HIS' }, { key: 'rejected', label: 'Từ chối / yêu cầu bổ sung' }];
      var maxStatus = statusRows.reduce(function (m, r) { return Math.max(m, c[r.key] || 0); }, 0) || 1;
      var statusList = h('div', { class: 'st-hbars' });
      statusRows.forEach(function (r) {
        var v = c[r.key] || 0;
        var fill = h('div', { class: 'st-hbar-fill ' + r.key });
        fill.style.width = Math.max(1, Math.round(v / maxStatus * 100)) + '%';
        statusList.appendChild(h('div', { class: 'st-hbar' },
          h('div', { class: 'st-hbar-label' }, statusBadge(r.key), h('span', { class: 'muted', text: ' ' + v })),
          h('div', { class: 'st-hbar-track' }, fill)));
      });
      var complaints = h('ol', { class: 'st-toplist' });
      (s.complaints || []).forEach(function (x) { complaints.appendChild(h('li', null, h('span', { text: x.text }), h('span', { class: 'muted', text: x.count + ' lượt' }))); });
      if (!(s.complaints || []).length) complaints.appendChild(h('li', { class: 'muted', text: 'Chưa có dữ liệu.' }));
      var devices = h('ul', { class: 'st-toplist' });
      (s.devices || []).forEach(function (d) { devices.appendChild(h('li', null, h('span', { text: d.device }), h('span', { class: 'muted', text: d.completed + ' hồ sơ · ' + d.fields + ' trường' }))); });
      if (!(s.devices || []).length) devices.appendChild(h('li', { class: 'muted', text: 'Chưa có máy bác sĩ hoàn tất hồ sơ.' }));
      var wrap = h('div', { class: 'st-detail st-dash' }, head, tiles,
        h('section', { class: 'card' }, h('div', { class: 'card-title' }, h('h3', { text: 'Tờ khai gửi theo ngày (' + days + ' ngày)' })), barChart(s.perDay || [], 'submitted', 'primary')),
        h('div', { class: 'grid-2 st-dash-grid' },
          h('section', { class: 'card' }, h('div', { class: 'card-title' }, h('h3', { text: 'Theo trạng thái (toàn bộ)' })), statusList),
          h('section', { class: 'card' }, h('div', { class: 'card-title' }, h('h3', { text: 'Lý do khám thường gặp' })), complaints),
          h('section', { class: 'card' }, h('div', { class: 'card-title' }, h('h3', { text: 'Máy bác sĩ đã nhập HIS' })), devices),
          h('section', { class: 'card' }, h('div', { class: 'card-title' }, h('h3', { text: 'Hôm nay' })),
            h('ul', { class: 'st-toplist' },
              h('li', null, h('span', { text: 'Tờ khai mới' }), h('span', { class: 'muted', text: String(t.submitted || 0) })),
              h('li', null, h('span', { text: 'Đã duyệt' }), h('span', { class: 'muted', text: String(t.approved || 0) })),
              h('li', null, h('span', { text: 'Đã nhập HIS' }), h('span', { class: 'muted', text: String(t.completed || 0) })),
              h('li', null, h('span', { text: 'Khai từ Internet' }), h('span', { class: 'muted', text: String(t.internet || 0) }))))),
        h('p', { class: 'hint', text: 'Thời gian tính từ lúc người bệnh gửi tờ khai đến lúc điều dưỡng duyệt, và từ lúc duyệt đến lúc bác sĩ xác nhận đã lưu trên HIS. Xuất Excel được ghi vào nhật ký thao tác.' }));
      els.main.appendChild(wrap);
      els.main.scrollTop = 0;
    }).catch(handleError);
  }

  function openAdmin(initial) {
    var isAdmin = user.role === 'admin';
    var tabsDef = [['access', 'Mã QR & kết nối']];
    if (isAdmin) tabsDef = tabsDef.concat([['devices', 'Máy bác sĩ'], ['users', 'Tài khoản'], ['settings', 'Cài đặt'], ['audit', 'Nhật ký']]);
    if (!tabsDef.some(function (t) { return t[0] === initial; })) initial = 'access';
    var tabBar = h('div', { class: 'st-admin-tabs', role: 'tablist' });
    var content = h('div');
    var dlg = modal(isAdmin ? 'Quản trị' : 'Mã QR & kết nối', [tabBar, content], [{ label: 'Đóng' }], true);
    function show(id) {
      clear(tabBar);
      tabsDef.forEach(function (t) {
        tabBar.appendChild(h('button', { type: 'button', role: 'tab', 'aria-selected': t[0] === id ? 'true' : 'false', text: t[1], onclick: function () { show(t[0]); } }));
      });
      clear(content);
      content.appendChild(h('div', { class: 'st-placeholder' }, h('span', { class: 'spinner' })));
      var loaders = { access: adminAccess, devices: adminDevices, users: adminUsers, settings: adminSettings, audit: adminAudit };
      loaders[id](content, dlg);
    }
    show(initial);
  }

  function qrCard(title, url, description, extra) {
    var box = h('div', { class: 'qr-box' });
    try { box.appendChild(window.qrSvg(url)); } catch (e) { box.appendChild(h('span', { class: 'muted', text: 'Không tạo được QR' })); }
    var print = h('button', { type: 'button', class: 'btn small', text: 'In áp phích A4', onclick: function () {
      window.open('/print?url=' + encodeURIComponent(url) + '&title=' + encodeURIComponent(title), '_blank', 'noopener');
    } });
    var copy = h('button', { type: 'button', class: 'btn small', text: 'Sao chép', onclick: function () {
      if (navigator.clipboard) navigator.clipboard.writeText(url).then(function () { toast('Đã sao chép địa chỉ.'); });
    } });
    return h('div', { class: 'st-access-card' },
      h('h3', { text: title }), description ? h('p', { class: 'hint', text: description }) : null,
      box, h('div', { class: 'url-text', text: url }), h('div', { class: 'row tight' }, print, copy, h('a', { class: 'btn small ghost', href: url, target: '_blank', rel: 'noopener', text: 'Mở' })),
      extra || null);
  }

  function adminAccess(content) {
    api('GET', '/api/staff/access').then(function (a) {
      clear(content);
      var lan = (a.lanPatientUrls || []).filter(function (u) { return u.indexOf('localhost') < 0; })[0] || (a.lanPatientUrls || [])[0] || '';
      if if (a.localOnly && !a.demoMode) content.appendChild(h('div', { class: 'alert warn' }, h('div', null, h('strong', { text: 'Máy chủ đang chỉ phục vụ trên chính máy này (localhost).' }),
        'Chạy install-server.ps1 bằng quyền Administrator để mở cổng cho mạng LAN và Internet.')));
      var grid = h('div', { class: 'st-access-grid' });
      if (lan) {
        grid.appendChild(qrCard('Máy tính bảng tại quầy (kiosk)', lan + '?kiosk=1', 'Mở trên máy tính bảng của bệnh viện. Tự xóa màn hình sau mỗi người để bảo mật.'));
        grid.appendChild(qrCard('Người bệnh quét bằng Wi-Fi bệnh viện', lan, 'Chỉ dùng được khi điện thoại bắt Wi-Fi nội bộ cùng mạng với máy chủ.'));
      }
      var tunnel = a.tunnel || {};
      var tunnelControls = null;
      if (user.role === 'admin') {
        var toggle = h('button', { type: 'button', class: 'btn small ' + (tunnel.mode === 'quick' ? 'danger' : 'primary'),
          text: tunnel.mode === 'quick' ? 'Tắt đường hầm tạm' : 'Bật đường hầm tạm (thử nghiệm)' });
        toggle.addEventListener('click', function () {
          toggle.disabled = true;
          api('POST', '/api/staff/tunnel', { mode: tunnel.mode === 'quick' ? 'off' : 'quick' }).then(function () {
            toast(tunnel.mode === 'quick' ? 'Đã tắt đường hầm.' : 'Đang bật đường hầm, chờ vài giây…');
            setTimeout(function () { adminAccess(content); }, tunnel.mode === 'quick' ? 300 : 4000);
          }).catch(handleError);
        });
        tunnelControls = h('div', { class: 'stack' },
          h('div', { class: 'hint', text: 'Trạng thái: ' + (tunnel.message || '') }),
          toggle,
          h('p', { class: 'hint', text: 'Đường hầm tạm (Cloudflare Quick Tunnel) đổi địa chỉ mỗi lần khởi động và chỉ nên dùng thử. Chính thức: tạo Cloudflare Tunnel có tên miền, cài bằng install-server.ps1 -TunnelToken, rồi nhập địa chỉ ở Cài đặt → Địa chỉ công khai. Chỉ cổng tờ khai người bệnh được mở ra Internet; trang điều dưỡng luôn chỉ trong LAN.' }));
      }
      if (a.internetUrl) {
        grid.appendChild(qrCard('Khai từ nhà qua Internet', a.internetUrl, 'Người bệnh khai trước khi đến bằng 4G/Wi-Fi nhà.', tunnelControls));
      } else {
        grid.appendChild(h('div', { class: 'st-access-card' }, h('h3', { text: 'Khai từ nhà qua Internet' }),
          h('p', { class: 'hint', text: 'Chưa bật. Người bệnh chỉ khai được trong mạng bệnh viện.' }), tunnelControls));
      }
      if (a.demoMode) {
        grid.appendChild(a.demoStaffUrl
          ? qrCard('Trang điều dưỡng qua Internet (CHẾ ĐỘ DEMO)', a.demoStaffUrl, 'Chỉ dùng với dữ liệu giả để trình diễn/chấm thi. Tắt ở Cài đặt khi dùng thật.')
          : h('div', { class: 'st-access-card' }, h('h3', { text: 'Trang điều dưỡng qua Internet (CHẾ ĐỘ DEMO)' }),
              h('p', { class: 'hint', text: 'Đang bật chế độ demo nhưng chưa có địa chỉ: ' + (a.demoStaffMessage || 'chờ đường hầm khởi động…') })));
      }
      content.appendChild(grid);
      var staffList = h('ul', null);
      (a.lanStaffUrls || []).forEach(function (u) { staffList.appendChild(h('li', null, h('span', { class: 'url-text', text: u }))); });
      content.appendChild(h('div', { class: 'card flat' }, h('h3', { text: 'Trang điều dưỡng trên máy khác trong LAN' }), staffList));
    }).catch(handleError);
  }

  function adminDevices(content) {
    api('GET', '/api/staff/devices').then(function (d) {
      clear(content);
      var codeArea = h('div');
      var gen = h('button', { type: 'button', class: 'btn primary', text: 'Tạo mã ghép nối máy bác sĩ' });
      gen.addEventListener('click', function () {
        api('POST', '/api/staff/devices/pair-code', {}).then(function (r) {
          clear(codeArea);
          var countdown = h('div', { class: 'hint' });
          var expires = parseTime(r.expiresAt);
          var timer = setInterval(function () {
            var left = Math.max(0, Math.round((expires - Date.now()) / 1000));
            countdown.textContent = left > 0 ? 'Hết hạn sau ' + Math.floor(left / 60) + ':' + two(left % 60) + ' · dùng được 1 lần' : 'Mã đã hết hạn.';
            if (!left || !document.body.contains(countdown)) clearInterval(timer);
          }, 500);
          var urls = h('ul', null);
          (r.serverUrls || []).forEach(function (u) { urls.appendChild(h('li', null, h('span', { class: 'url-text', text: u }))); });
          codeArea.appendChild(h('div', { class: 'stack' },
            h('div', { class: 'st-pair-code', text: r.code.slice(0, 3) + ' ' + r.code.slice(3) }), countdown,
            h('ol', null,
              h('li', { text: 'Trên máy bác sĩ, mở HIS Admission Assistant → tab "Tờ khai BN" → Ghép nối.' }),
              h('li', { text: 'Bấm "Tự tìm máy chủ" hoặc nhập một địa chỉ bên dưới.' }),
              h('li', { text: 'Nhập mã 6 số ở trên. Máy sẽ được ghi nhớ, không cần nhập lại.' })),
            urls));
        }).catch(handleError);
      });
      var table = h('table', { class: 'st-table' },
        h('thead', null, h('tr', null, ['Tên máy', 'Máy tính', 'Ghép lúc', 'Hoạt động gần nhất', 'IP', ''].map(function (t) { return h('th', { text: t }); }))));
      var body = h('tbody');
      (d.items || []).forEach(function (dev) {
        var revoke = dev.revoked ? h('span', { class: 'badge rejected', text: 'Đã thu hồi' })
          : h('button', { type: 'button', class: 'btn danger small', text: 'Thu hồi', onclick: function () {
            confirmDialog('Thu hồi máy ' + dev.name + '?', 'Máy này sẽ không nhận được tờ khai cho tới khi ghép nối lại.', 'Thu hồi', function () {
              api('POST', '/api/staff/devices/' + dev.id + '/revoke', {}).then(function () { toast('Đã thu hồi.'); adminDevices(content); }).catch(handleError);
            }, 'danger');
          } });
        body.appendChild(h('tr', null, h('td', { text: dev.name }), h('td', { text: dev.machineName }), h('td', { text: dateTime(dev.createdAt) }),
          h('td', { text: ago(dev.lastSeenAt) }), h('td', { class: 'mono', text: dev.lastIp }), h('td', null, revoke)));
      });
      if (!(d.items || []).length) body.appendChild(h('tr', null, h('td', { colspan: '6', class: 'muted', text: 'Chưa có máy bác sĩ nào được ghép nối.' })));
      table.appendChild(body);
      content.appendChild(h('div', { class: 'stack loose' },
        h('div', { class: 'stack' }, h('p', { class: 'muted', text: 'Mỗi máy bác sĩ chạy HIS Admission Assistant cần ghép nối một lần. Khóa của máy được lưu mã hóa trên máy đó và có thể thu hồi bất cứ lúc nào.' }), gen, codeArea),
        h('div', { class: 'st-table-wrap' }, table)));
    }).catch(handleError);
  }

  function adminUsers(content) {
    api('GET', '/api/staff/users').then(function (d) {
      clear(content);
      var table = h('table', { class: 'st-table' },
        h('thead', null, h('tr', null, ['Tên đăng nhập', 'Tên hiển thị', 'Vai trò', 'Trạng thái', ''].map(function (t) { return h('th', { text: t }); }))));
      var body = h('tbody');
      (d.items || []).forEach(function (u) {
        body.appendChild(h('tr', null, h('td', { class: 'mono', text: u.username }), h('td', { text: u.displayName }),
          h('td', { text: u.role === 'admin' ? 'Quản trị' : 'Điều dưỡng' }),
          h('td', null, u.disabled ? h('span', { class: 'badge rejected', text: 'Đã khóa' }) : u.mustChangePassword ? h('span', { class: 'badge pending', text: 'Chờ đổi mật khẩu' }) : h('span', { class: 'badge completed', text: 'Hoạt động' })),
          h('td', null, h('button', { type: 'button', class: 'btn small', text: 'Sửa', onclick: function () { userForm(content, u); } }))));
      });
      table.appendChild(body);
      content.appendChild(h('div', { class: 'stack' },
        h('div', { class: 'row between' }, h('p', { class: 'muted', text: 'Tài khoản mới dùng mật khẩu tạm và phải đổi ở lần đăng nhập đầu.' }),
          h('button', { type: 'button', class: 'btn primary', text: '+ Thêm tài khoản', onclick: function () { userForm(content, null); } })),
        h('div', { class: 'st-table-wrap' }, table)));
    }).catch(handleError);
  }

  function userForm(content, existing) {
    var usern = h('input', { id: 'uf-u', type: 'text', value: existing ? existing.username : '', readonly: existing ? true : null, autocomplete: 'off' });
    var name = h('input', { id: 'uf-n', type: 'text', value: existing ? existing.displayName : '' });
    var role = h('select', { id: 'uf-r' }, h('option', { value: 'nurse', text: 'Điều dưỡng' }), h('option', { value: 'admin', text: 'Quản trị' }));
    role.value = existing ? existing.role : 'nurse';
    var pass = h('input', { id: 'uf-p', type: 'text', autocomplete: 'off', placeholder: existing ? 'Bỏ trống nếu không đặt lại' : 'Mật khẩu tạm' });
    var disabled = h('input', { id: 'uf-d', type: 'checkbox' });
    disabled.checked = existing ? !!existing.disabled : false;
    var skipChange = h('input', { id: 'uf-s', type: 'checkbox' });
    modal(existing ? 'Sửa tài khoản' : 'Thêm tài khoản', h('div', { class: 'stack' },
      h('div', { class: 'grid-2' }, field('Tên đăng nhập', usern, existing ? null : 'Chữ thường không dấu, ví dụ: lan.nt', 'uf-u'), field('Tên hiển thị', name, null, 'uf-n')),
      h('div', { class: 'grid-2' }, field('Vai trò', role, null, 'uf-r'), field(existing ? 'Đặt lại mật khẩu tạm' : 'Mật khẩu tạm', pass, 'Tối thiểu 8 ký tự, có chữ và số.', 'uf-p')),
      existing ? h('label', { class: 'check', for: 'uf-d' }, disabled, h('span', { text: 'Khóa tài khoản' })) : null,
      existing ? null : h('label', { class: 'check', for: 'uf-s' }, skipChange, h('span', { text: 'Tài khoản demo: không bắt đổi mật khẩu ở lần đăng nhập đầu (ví dụ cấp cho Ban Giám khảo).' }))), [
      { label: 'Hủy' },
      { label: 'Lưu', kind: 'primary', onClick: function (close) {
        api('POST', '/api/staff/users', { username: usern.value.trim(), displayName: name.value.trim(), role: role.value, password: pass.value, disabled: disabled.checked, skipPasswordChange: skipChange.checked })
          .then(function () { close(); toast('Đã lưu tài khoản.', 'ok'); adminUsers(content); })
          .catch(handleError);
        return false;
      } }
    ]);
  }

  function adminSettings(content) {
    api('GET', '/api/staff/settings').then(function (s) {
      clear(content);
      var inputs = {
        hospitalName: h('input', { id: 'se-h', type: 'text', value: s.hospitalName }),
        departmentName: h('input', { id: 'se-d', type: 'text', value: s.departmentName, placeholder: 'Ví dụ: Phòng khám Ngoại' }),
        publicBaseUrl: h('input', { id: 'se-u', type: 'url', value: s.publicBaseUrl, placeholder: 'https://khai.benhvien.vn' }),
        retentionDaysPending: h('input', { id: 'se-rp', type: 'number', min: 1, max: 30, value: s.retentionDaysPending }),
        retentionDaysDone: h('input', { id: 'se-rd', type: 'number', min: 1, max: 90, value: s.retentionDaysDone }),
        maxPending: h('input', { id: 'se-mp', type: 'number', min: 10, max: 5000, value: s.maxPending }),
        publicSubmitLimitPerIp: h('input', { id: 'se-li', type: 'number', min: 1, max: 100, value: s.publicSubmitLimitPerIp })
      };
      var requireId = h('input', { id: 'se-req', type: 'checkbox' });
      requireId.checked = !!s.requireHisPatientIdOnApprove;
      var demoMode = h('input', { id: 'se-demo', type: 'checkbox' });
      demoMode.checked = !!s.allowPublicStaffAccess;
      var aiEnabled = h('input', { id: 'se-ai', type: 'checkbox' });
      aiEnabled.checked = !!s.aiEnabled;
      var aiModel = h('input', { id: 'se-aim', type: 'text', value: s.aiModel || '', placeholder: 'claude-sonnet-4-5' });
      var aiEndpoint = h('input', { id: 'se-aie', type: 'url', value: s.aiEndpoint || '', placeholder: 'Mặc định: https://api.anthropic.com/v1/messages' });
      var aiKey = h('input', { id: 'se-aik', type: 'password', autocomplete: 'off', placeholder: s.aiKeySet ? 'Đã lưu (mã hóa). Nhập để thay, gõ - để xóa' : 'sk-ant-…' });
      var aiTest = h('button', { type: 'button', class: 'btn small', text: 'Kiểm tra kết nối AI' });
      aiTest.addEventListener('click', function () {
        aiTest.disabled = true;
        api('POST', '/api/staff/ai/test', {}).then(function (r) { aiTest.disabled = false; toast('AI trả lời: "' + r.reply + '" (' + r.model + ')', 'ok'); })
          .catch(function (err) { aiTest.disabled = false; handleError(err); });
      });
      var seedBtn = h('button', { type: 'button', class: 'btn small', text: 'Tạo 14 tờ khai mẫu (dữ liệu giả)' });
      seedBtn.addEventListener('click', function () {
        confirmDialog('Tạo dữ liệu mẫu?', 'Thêm 14 tờ khai giả (tên "Mẫu/Thử/Demo", số điện thoại 0900000xxx) ở đủ các trạng thái để trình diễn. Không dùng trên máy chủ thật.', 'Tạo', function () {
          api('POST', '/api/staff/demo-data', {}).then(function (r) { toast('Đã tạo ' + r.created + ' tờ khai mẫu.', 'ok'); refreshList(true); }).catch(handleError);
        });
      });
      var saveBtn = h('button', { type: 'button', class: 'btn primary', text: 'Lưu cài đặt' });
      saveBtn.addEventListener('click', function () {
        var body = { requireHisPatientIdOnApprove: requireId.checked, allowPublicStaffAccess: demoMode.checked,
          aiEnabled: aiEnabled.checked, aiModel: aiModel.value.trim(), aiEndpoint: aiEndpoint.value.trim(), aiApiKey: aiKey.value.trim() };
        Object.keys(inputs).forEach(function (k) { body[k] = inputs[k].type === 'number' ? Number(inputs[k].value) : inputs[k].value.trim(); });
        api('POST', '/api/staff/settings', body).then(function (r) {
          boot.hospitalName = r.hospitalName;
          boot.departmentName = r.departmentName;
          boot.aiEnabled = !!(r.aiEnabled && r.aiKeySet);
          aiKey.value = '';
          aiKey.placeholder = r.aiKeySet ? 'Đã lưu (mã hóa). Nhập để thay, gõ - để xóa' : 'sk-ant-…';
          toast('Đã lưu cài đặt.', 'ok');
        }).catch(handleError);
      });
      content.appendChild(h('div', { class: 'stack loose' },
        h('div', { class: 'grid-2' },
          field('Tên bệnh viện / cơ sở', inputs.hospitalName, null, 'se-h'),
          field('Khoa / phòng khám', inputs.departmentName, null, 'se-d')),
        field('Địa chỉ công khai của tờ khai (Internet)', inputs.publicBaseUrl, 'Địa chỉ https của Cloudflare Tunnel có tên miền hoặc reverse proxy trỏ vào cổng người bệnh (' + s.publicPort + '). Dùng để tạo mã QR.', 'se-u'),
        h('div', { class: 'grid-4 keep-2' },
          field('Giữ tờ khai chưa duyệt (ngày)', inputs.retentionDaysPending, null, 'se-rp'),
          field('Giữ tờ khai đã xong (ngày)', inputs.retentionDaysDone, null, 'se-rd'),
          field('Tối đa tờ khai chờ duyệt', inputs.maxPending, null, 'se-mp'),
          field('Giới hạn gửi / IP / 10 phút', inputs.publicSubmitLimitPerIp, null, 'se-li')),
        h('label', { class: 'check', for: 'se-req' }, requireId, h('span', { text: 'Bắt buộc nhập mã BN trên HIS trước khi duyệt (khuyên dùng khi quầy tiếp nhận luôn tạo hồ sơ trước).' })),
        h('label', { class: 'check', for: 'se-demo' }, demoMode, h('span', { text: 'CHẾ ĐỘ DEMO: cho phép mở trang điều dưỡng qua Internet (đường hầm Cloudflare thứ hai). Chỉ dùng với dữ liệu giả; tắt khi vận hành thật.' })),
        h('div', { class: 'row tight' }, seedBtn, h('span', { class: 'hint', text: 'Dữ liệu mẫu phục vụ trình diễn và huấn luyện điều dưỡng.' })),
        h('div', { class: 'card flat' },
          h('h3', { text: 'Trợ lý AI có kiểm soát' }),
          h('p', { class: 'hint', text: 'Dùng Anthropic Claude API. Khóa API được lưu mã hóa (DPAPI) trên máy chủ và không bao giờ hiển thị lại. AI chỉ nhận dữ liệu lâm sàng đã bỏ định danh của từng tờ khai; kết quả phải được điều dưỡng duyệt từng mục. Lưu cài đặt rồi bấm Kiểm tra kết nối.' }),
          h('label', { class: 'check', for: 'se-ai' }, aiEnabled, h('span', { text: 'Bật trợ lý AI trên trang điều dưỡng' })),
          h('div', { class: 'grid-2' }, field('Khóa API (Anthropic)', aiKey, null, 'se-aik'), field('Mô hình', aiModel, 'Ví dụ claude-sonnet-4-5 hoặc claude-haiku-4-5 (rẻ, nhanh).', 'se-aim')),
          field('Địa chỉ dịch vụ (nâng cao)', aiEndpoint, 'Bỏ trống để dùng API Anthropic chính thức; điền khi dùng máy chủ trung gian của bệnh viện.', 'se-aie'),
          h('div', { class: 'row tight' }, aiTest)),
        h('div', { class: 'alert info' }, h('div', null, h('strong', { text: 'Thông tin kỹ thuật' }),
          'Cổng nhân viên: ' + s.staffPort + ' · Cổng người bệnh: ' + s.publicPort + ' · Dữ liệu (mã hóa DPAPI): ' + s.dataDirectory)),
        h('div', { class: 'row end' }, saveBtn)));
    }).catch(handleError);
  }

  function adminAudit(content) {
    api('GET', '/api/staff/audit?limit=400').then(function (d) {
      clear(content);
      var table = h('table', { class: 'st-table st-audit' },
        h('thead', null, h('tr', null, ['Thời gian', 'Người/máy', 'Thao tác', 'Đối tượng', 'IP'].map(function (t) { return h('th', { text: t }); }))));
      var body = h('tbody');
      (d.items || []).forEach(function (line) {
        var parts = line.split('\t');
        body.appendChild(h('tr', null, parts.slice(0, 5).map(function (p) { return h('td', { text: p }); })));
      });
      table.appendChild(body);
      content.appendChild(h('div', { class: 'stack' },
        h('p', { class: 'hint', text: 'Nhật ký tháng này (không chứa nội dung lâm sàng). Tệp đầy đủ nằm trong thư mục logs của máy chủ.' }),
        h('div', { class: 'st-table-wrap' }, table)));
    }).catch(handleError);
  }

  // ------------------------------------------------------------------ keyboard
  document.addEventListener('keydown', function (e) {
    if (!user || !els.main || document.querySelector('.modal-backdrop')) return;
    var inField = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement && document.activeElement.tagName);
    if (e.key === '/' && !inField) { e.preventDefault(); els.search.focus(); return; }
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter' && editable()) { e.preventDefault(); save('approve'); return; }
    if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S') && editable()) { e.preventDefault(); save('save'); }
  });
  window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

  // ------------------------------------------------------------------ boot
  function loadBoot() {
    return api('GET', '/api/staff/bootstrap').then(function (b) { boot = b; if (b.user) user = b.user; return b; });
  }

  loadBoot().then(function (b) {
    if (b.setupRequired) renderSetup();
    else if (!user) renderLogin();
    else if (user.mustChangePassword) renderChangePassword(true);
    else renderMain();
  }).catch(function (err) {
    clear(root);
    root.appendChild(h('div', { class: 'st-center' }, h('div', { class: 'alert danger' }, h('div', null,
      h('strong', { text: 'Không mở được trang điều dưỡng' }), err && err.message ? err.message : 'Không kết nối được máy chủ.'))));
  });
})();
