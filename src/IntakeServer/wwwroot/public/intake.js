/* UMC2 — Tờ khai trước khám (patient-facing). Vanilla JS, no external dependencies, CSP-safe (no inline code). */
(function () {
  'use strict';

  var params = new URLSearchParams(location.search);
  var KIOSK = params.get('kiosk') === '1';
  var DRAFT_KEY = 'umc2-intake-draft-v1';
  var app = document.getElementById('app');
  var toasts = document.getElementById('toasts');
  var restartBtn = document.getElementById('restartBtn');

  var config = { hospitalName: 'Bệnh viện', departmentName: '', formVersion: '2026.09' };
  var state = emptyState();
  var stepIndex = 0;
  var errors = {};
  var submitting = false;
  var lastActivity = Date.now();
  var idleModal = null;

  // ------------------------------------------------------------------ form definition
  var COMPLAINTS = ['Đau bụng', 'Sốt', 'Ho', 'Khó thở', 'Đau ngực', 'Đau đầu', 'Chóng mặt', 'Đau lưng',
    'Đau khớp', 'Tiểu buốt, tiểu rắt', 'Tiểu máu', 'Buồn nôn, nôn', 'Tiêu chảy', 'Mệt mỏi', 'Nổi mẩn, ngứa',
    'Khám sức khỏe', 'Tái khám theo hẹn'];
  var ASSOCIATED = ['Sốt', 'Ớn lạnh', 'Ho', 'Khó thở', 'Đau ngực', 'Hồi hộp, tim đập nhanh', 'Buồn nôn, nôn',
    'Đau bụng', 'Tiêu chảy', 'Táo bón', 'Đi ngoài phân đen hoặc có máu', 'Tiểu buốt, tiểu rắt', 'Tiểu máu',
    'Phù chân', 'Đau đầu', 'Chóng mặt', 'Sụt cân', 'Chán ăn', 'Mất ngủ', 'Tê, yếu tay chân'];
  var CHRONIC = ['Tăng huyết áp', 'Đái tháo đường', 'Rối loạn mỡ máu', 'Bệnh tim mạch', 'Đột quỵ', 'Hen, COPD',
    'Lao', 'Viêm gan B/C', 'Bệnh gan khác', 'Bệnh thận', 'Sỏi thận, tiết niệu', 'Viêm loét dạ dày', 'Gút',
    'Bệnh tuyến giáp', 'Ung thư', 'Rối loạn đông máu'];
  var FAMILY = ['Tăng huyết áp', 'Đái tháo đường', 'Bệnh tim mạch', 'Đột quỵ', 'Ung thư', 'Hen', 'Bệnh thận', 'Bệnh di truyền'];
  var ONSET_UNITS = ['giờ', 'ngày', 'tuần', 'tháng', 'năm'];

  function isFemaleOfChildbearingAge(s) {
    var age = ageOf(s);
    return s.patient.gender === 'female' && age !== null && age >= 12 && age <= 55;
  }

  var STEPS = [
    {
      id: 'patient', title: 'Thông tin người bệnh',
      intro: 'Khoảng 3–5 phút. Thông tin giúp bác sĩ khám nhanh và chính xác hơn; nhân viên y tế sẽ kiểm tra lại trước khi đưa vào hồ sơ.',
      emergency: true,
      fields: [
        { key: 'filledBy', path: 'patient.filledBy', type: 'choice', label: 'Ai đang khai?', required: true,
          options: [['self', 'Tôi là người bệnh'], ['relative', 'Khai giúp người nhà']] },
        { key: 'relation', path: 'patient.relation', type: 'text', label: 'Bạn là gì của người bệnh?', maxLength: 40,
          placeholder: 'Ví dụ: con, vợ, chồng, cha, mẹ', showIf: function (s) { return s.patient.filledBy === 'relative'; } },
        { key: 'fullName', path: 'patient.fullName', type: 'text', label: 'Họ và tên người bệnh', required: true, maxLength: 80,
          autocomplete: 'name', placeholder: 'Ví dụ: Nguyễn Văn An', titleCase: true },
        { key: 'birth', type: 'date3', label: 'Ngày sinh', required: true, hint: 'Nếu không nhớ ngày, tháng thì chỉ cần nhập năm sinh.' },
        { key: 'gender', path: 'patient.gender', type: 'choice', label: 'Giới tính', required: true,
          options: [['male', 'Nam'], ['female', 'Nữ'], ['other', 'Khác']] },
        { key: 'phone', path: 'patient.phone', type: 'tel', label: 'Số điện thoại liên hệ', required: true, autocomplete: 'tel',
          placeholder: '09xx xxx xxx', validate: validatePhone },
        { key: 'nationalId', path: 'patient.nationalId', type: 'text', inputmode: 'numeric', label: 'Số CCCD', maxLength: 14,
          hint: 'Không bắt buộc — giúp đối chiếu đúng người bệnh.', validate: validateNationalId },
        { key: 'hisPatientId', path: 'patient.hisPatientId', type: 'text', label: 'Mã bệnh nhân của bệnh viện (nếu có)', maxLength: 30,
          hint: 'In trên thẻ khám hoặc phiếu khám cũ. Bỏ trống nếu không có.' }
      ]
    },
    {
      id: 'complaint', title: 'Lý do đến khám', emergency: true,
      fields: [
        { key: 'chiefComplaint', path: 'answers.chiefComplaint', type: 'suggestText', label: 'Lý do chính khiến bạn đi khám hôm nay',
          required: true, maxLength: 300, suggestions: COMPLAINTS, placeholder: 'Mô tả ngắn gọn, ví dụ: đau bụng vùng dưới rốn' },
        { key: 'onset', type: 'onset', label: 'Triệu chứng bắt đầu cách đây bao lâu?', required: true },
        { key: 'symptomDescription', path: 'answers.symptomDescription', type: 'textarea', label: 'Mô tả thêm (nếu có)', maxLength: 1500,
          placeholder: 'Vị trí, tính chất, lúc nào tăng hay giảm, diễn tiến từ lúc bắt đầu đến nay…' },
        { key: 'painScore', path: 'answers.painScore', type: 'range', label: 'Mức độ đau hoặc khó chịu hiện tại' },
        { key: 'associatedSymptoms', path: 'answers.associatedSymptoms', type: 'chips', label: 'Triệu chứng đi kèm', options: ASSOCIATED,
          hint: 'Chọn tất cả triệu chứng đang có.' },
        { key: 'associatedOther', path: 'answers.associatedOther', type: 'text', label: 'Triệu chứng khác', maxLength: 200 },
        { key: 'priorTreatment', path: 'answers.priorTreatment', type: 'choice', label: 'Bạn đã điều trị gì chưa?',
          options: [['Chưa điều trị', 'Chưa'], ['Tự mua thuốc', 'Tự mua thuốc'], ['Đã khám nơi khác', 'Đã khám nơi khác']] },
        { key: 'priorTreatmentDetail', path: 'answers.priorTreatmentDetail', type: 'textarea', label: 'Thuốc đã dùng / nơi đã khám, kết quả',
          maxLength: 1000, showIf: function (s) { return !!s.answers.priorTreatment && s.answers.priorTreatment !== 'Chưa điều trị'; } }
      ]
    },
    {
      id: 'history', title: 'Tiền sử & thuốc đang dùng',
      fields: [
        { key: 'chronicConditions', path: 'answers.chronicConditions', type: 'chips', label: 'Bạn đang hoặc đã từng mắc bệnh nào?',
          options: CHRONIC, noneOption: 'Không có bệnh nào', required: true, requiredMessage: 'Vui lòng chọn bệnh hoặc "Không có bệnh nào".' },
        { key: 'chronicOther', path: 'answers.chronicOther', type: 'text', label: 'Bệnh khác', maxLength: 300 },
        { key: 'surgeries', path: 'answers.surgeries', type: 'textarea', label: 'Phẫu thuật, thủ thuật đã làm', maxLength: 800,
          placeholder: 'Tên phẫu thuật và năm, ví dụ: mổ ruột thừa 2015' },
        { key: 'medications', path: 'answers.medications', type: 'textarea', label: 'Thuốc đang dùng thường xuyên', maxLength: 1000,
          placeholder: 'Tên thuốc – liều – số lần mỗi ngày. Có thể mang toa thuốc cho bác sĩ xem.' },
        { key: 'smoking', path: 'answers.smoking', type: 'choice', label: 'Hút thuốc lá',
          options: [['Không', 'Không'], ['Đã bỏ', 'Đã bỏ'], ['Đang hút', 'Đang hút']] },
        { key: 'alcohol', path: 'answers.alcohol', type: 'choice', label: 'Uống rượu bia',
          options: [['Không', 'Không'], ['Thỉnh thoảng', 'Thỉnh thoảng'], ['Thường xuyên', 'Thường xuyên']] },
        { key: 'pregnancy', path: 'answers.pregnancy', type: 'choice', label: 'Hiện có đang mang thai không?', showIf: isFemaleOfChildbearingAge,
          options: [['Không', 'Không'], ['Có', 'Có'], ['Không chắc', 'Không chắc']] },
        { key: 'lastMenstrualPeriod', path: 'answers.lastMenstrualPeriod', type: 'date', label: 'Ngày đầu tiên của kỳ kinh gần nhất',
          showIf: isFemaleOfChildbearingAge }
      ]
    },
    {
      id: 'allergy', title: 'Dị ứng & gia đình',
      fields: [
        { key: 'allergyStatus', path: 'answers.allergyStatus', type: 'choice', label: 'Bạn có bị dị ứng thuốc, thức ăn hay thứ gì khác không?',
          required: true, options: [['Không', 'Không'], ['Có', 'Có'], ['Không rõ', 'Không rõ']] },
        { key: 'allergyDrugs', path: 'answers.allergyDrugs', type: 'text', label: 'Dị ứng thuốc gì?', maxLength: 300,
          placeholder: 'Ví dụ: Amoxicillin, Aspirin', showIf: allergyYes },
        { key: 'allergyFoods', path: 'answers.allergyFoods', type: 'text', label: 'Dị ứng thức ăn gì?', maxLength: 300, showIf: allergyYes },
        { key: 'allergyOther', path: 'answers.allergyOther', type: 'text', label: 'Dị ứng khác', maxLength: 300,
          placeholder: 'Ví dụ: phấn hoa, cao su, thuốc cản quang', showIf: allergyYes },
        { key: 'allergyReaction', path: 'answers.allergyReaction', type: 'text', label: 'Khi dị ứng thì bị gì?', maxLength: 300,
          placeholder: 'Ví dụ: nổi mẩn, sưng môi, khó thở', showIf: allergyYes },
        { key: 'familyConditions', path: 'answers.familyConditions', type: 'chips', label: 'Cha mẹ, anh chị em ruột có ai mắc bệnh sau?',
          options: FAMILY, noneOption: 'Không có / không rõ' },
        { key: 'familyOther', path: 'answers.familyOther', type: 'text', label: 'Bệnh khác trong gia đình', maxLength: 300 },
        { key: 'body', type: 'body', label: 'Cân nặng và chiều cao (nếu biết)' },
        { key: 'notes', path: 'answers.notes', type: 'textarea', label: 'Điều bạn muốn bác sĩ biết thêm', maxLength: 1000 }
      ]
    },
    { id: 'review', title: 'Xem lại & gửi', review: true }
  ];

  function allergyYes(s) { return s.answers.allergyStatus === 'Có'; }

  // ------------------------------------------------------------------ state helpers
  function emptyState() {
    return {
      patient: { filledBy: 'self', relation: '', fullName: '', birthDay: '', birthMonth: '', birthYear: '', gender: '', phone: '', nationalId: '', hisPatientId: '' },
      answers: {
        chiefComplaint: '', onsetValue: '', onsetUnit: 'ngày', symptomDescription: '', painScore: null, associatedSymptoms: [],
        associatedOther: '', priorTreatment: '', priorTreatmentDetail: '', chronicConditions: [], chronicOther: '', surgeries: '',
        medications: '', smoking: '', alcohol: '', pregnancy: '', lastMenstrualPeriod: '', allergyStatus: '', allergyDrugs: '',
        allergyFoods: '', allergyOther: '', allergyReaction: '', familyConditions: [], familyOther: '', weightKg: '', heightCm: '', notes: ''
      },
      consent: false
    };
  }

  function get(path) {
    var parts = path.split('.');
    return state[parts[0]][parts[1]];
  }

  function set(path, value) {
    var parts = path.split('.');
    state[parts[0]][parts[1]] = value;
    touched();
  }

  function ageOf(s) {
    var year = parseInt(s.patient.birthYear, 10);
    if (!year || year < 1900) return null;
    return new Date().getFullYear() - year;
  }

  function hasContent() {
    return !!(state.patient.fullName || state.patient.phone || state.answers.chiefComplaint);
  }

  var saveTimer = null;
  function touched() {
    lastActivity = Date.now();
    restartBtn.hidden = !hasContent();
    if (KIOSK) return;
    clearTimeout(saveTimer);
    saveTimer = setTimeout(function () {
      try { sessionStorage.setItem(DRAFT_KEY, JSON.stringify({ state: state, step: stepIndex, at: Date.now() })); } catch (e) { /* storage unavailable */ }
    }, 250);
  }

  function clearDraft() {
    try { sessionStorage.removeItem(DRAFT_KEY); } catch (e) { /* ignore */ }
  }

  function loadDraft() {
    if (KIOSK) return null;
    try {
      var raw = sessionStorage.getItem(DRAFT_KEY);
      if (!raw) return null;
      var draft = JSON.parse(raw);
      if (!draft || !draft.state || Date.now() - draft.at > 6 * 3600 * 1000) return null;
      return draft;
    } catch (e) {
      return null;
    }
  }

  // ------------------------------------------------------------------ DOM helpers
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
    for (var i = 2; i < arguments.length; i++) append(el, arguments[i]);
    return el;
  }

  function append(el, child) {
    if (child === null || child === undefined || child === false) return;
    if (Array.isArray(child)) { child.forEach(function (c) { append(el, c); }); return; }
    el.appendChild(typeof child === 'string' ? document.createTextNode(child) : child);
  }

  function toast(message, kind) {
    var t = h('div', { class: 'toast' + (kind ? ' ' + kind : ''), text: message });
    toasts.appendChild(t);
    setTimeout(function () { t.remove(); }, kind === 'error' ? 6000 : 3500);
  }

  // ------------------------------------------------------------------ validation
  function validatePhone(value) {
    var digits = (value || '').replace(/\D/g, '');
    if (digits.indexOf('84') === 0 && digits.length >= 11) digits = '0' + digits.slice(2);
    if (digits.length < 9 || digits.length > 11) return 'Số điện thoại chưa đúng (9–11 chữ số).';
    return null;
  }

  function validateNationalId(value) {
    var digits = (value || '').replace(/\D/g, '');
    if (!digits) return null;
    if (digits.length !== 9 && digits.length !== 12) return 'Số CCCD/CMND gồm 12 (hoặc 9) chữ số.';
    return null;
  }

  function validateBirth() {
    var p = state.patient;
    var year = parseInt(p.birthYear, 10);
    var now = new Date();
    if (!p.birthYear) return 'Vui lòng nhập năm sinh.';
    if (!/^\d{4}$/.test(p.birthYear) || year < 1900 || year > now.getFullYear()) return 'Năm sinh chưa đúng.';
    if (p.birthDay || p.birthMonth) {
      var d = parseInt(p.birthDay, 10), m = parseInt(p.birthMonth, 10);
      if (!d || !m || m < 1 || m > 12 || d < 1 || d > 31) return 'Ngày/tháng sinh chưa đúng. Có thể bỏ trống ngày, tháng.';
      var date = new Date(year, m - 1, d);
      if (date.getMonth() !== m - 1 || date > now) return 'Ngày sinh không tồn tại.';
    }
    return null;
  }

  function isVisible(field) {
    return !field.showIf || field.showIf(state);
  }

  function fieldError(field) {
    if (!isVisible(field)) return null;
    if (field.type === 'date3') return validateBirth();
    if (field.type === 'onset') {
      var v = state.answers.onsetValue;
      if (v === '' || v === null) return field.required ? 'Vui lòng cho biết thời gian bắt đầu (ước lượng cũng được).' : null;
      var n = Number(v);
      if (!isFinite(n) || n < 0 || n > 1000) return 'Thời gian chưa hợp lệ.';
      return null;
    }
    if (field.type === 'body') {
      var w = state.answers.weightKg, ht = state.answers.heightCm;
      if (w !== '' && (Number(w) < 1 || Number(w) > 300)) return 'Cân nặng chưa hợp lệ (kg).';
      if (ht !== '' && (Number(ht) < 30 || Number(ht) > 250)) return 'Chiều cao chưa hợp lệ (cm).';
      return null;
    }
    var value = field.path ? get(field.path) : null;
    var empty = value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0);
    if (field.required && empty) return field.requiredMessage || (field.type === 'choice' ? 'Vui lòng chọn một mục.' : 'Vui lòng điền mục này.');
    if (!empty && field.validate) return field.validate(value);
    return null;
  }

  function validateStep(step) {
    errors = {};
    var first = null;
    (step.fields || []).forEach(function (field) {
      var err = fieldError(field);
      if (err) {
        errors[field.key] = err;
        if (!first) first = field.key;
      }
    });
    return first;
  }

  // ------------------------------------------------------------------ field renderers
  function fieldShell(field, control, labelFor) {
    var err = errors[field.key];
    var hintId = 'hint-' + field.key;
    var errId = 'err-' + field.key;
    var labelEl = labelFor
      ? h('label', { for: labelFor, class: field.required ? 'req' : null, text: field.label })
      : h('div', { class: 'label' + (field.required ? ' req' : ''), id: 'lbl-' + field.key, text: field.label });
    var wrap = h('div', { class: 'field', 'data-key': field.key },
      labelEl,
      field.hint ? h('div', { class: 'hint', id: hintId, text: field.hint }) : null,
      control,
      err ? h('div', { class: 'error-text', id: errId, role: 'alert', text: err }) : null);
    return wrap;
  }

  function describedBy(field) {
    var ids = [];
    if (field.hint) ids.push('hint-' + field.key);
    if (errors[field.key]) ids.push('err-' + field.key);
    return ids.length ? ids.join(' ') : null;
  }

  function renderText(field) {
    var id = 'f-' + field.key;
    var type = field.type === 'tel' ? 'tel' : 'text';
    var input = h('input', {
      id: id, type: type, value: get(field.path) || '', maxlength: field.maxLength || 200,
      autocomplete: field.autocomplete || 'off', inputmode: field.inputmode || (type === 'tel' ? 'tel' : null),
      placeholder: field.placeholder || null, 'aria-invalid': errors[field.key] ? 'true' : null,
      'aria-describedby': describedBy(field), 'aria-required': field.required ? 'true' : null
    });
    input.addEventListener('input', function () { set(field.path, input.value); });
    if (field.titleCase) {
      input.addEventListener('blur', function () {
        var v = titleCase(input.value);
        input.value = v;
        set(field.path, v);
      });
    }
    return fieldShell(field, input, id);
  }

  function renderTextarea(field) {
    var id = 'f-' + field.key;
    var input = h('textarea', {
      id: id, maxlength: field.maxLength || 1000, placeholder: field.placeholder || null,
      'aria-invalid': errors[field.key] ? 'true' : null, 'aria-describedby': describedBy(field), rows: 3
    });
    input.value = get(field.path) || '';
    input.addEventListener('input', function () { set(field.path, input.value); });
    return fieldShell(field, input, id);
  }

  function renderChoice(field) {
    var current = get(field.path);
    var longLabels = field.options.length >= 3 && field.options.some(function (o) { return o[1].length > 10; });
    var group = h('div', { class: 'segmented' + (field.options.length > 3 ? ' wrap' : '') + (longLabels ? ' long' : ''), role: 'radiogroup', 'aria-labelledby': 'lbl-' + field.key,
      'aria-describedby': describedBy(field) });
    field.options.forEach(function (opt) {
      var btn = h('button', { type: 'button', role: 'radio', 'aria-checked': current === opt[0] ? 'true' : 'false', text: opt[1] });
      btn.addEventListener('click', function () {
        set(field.path, current === opt[0] && !field.required ? '' : opt[0]);
        rerender(field.key);
      });
      group.appendChild(btn);
    });
    return fieldShell(field, group);
  }

  function renderChips(field) {
    var selected = (get(field.path) || []).slice();
    var group = h('div', { class: 'chips', role: 'group', 'aria-labelledby': 'lbl-' + field.key, 'aria-describedby': describedBy(field) });
    var all = field.options.slice();
    if (field.noneOption) all.push(field.noneOption);
    all.forEach(function (opt) {
      var on = selected.indexOf(opt) >= 0;
      var chip = h('button', { type: 'button', class: 'chip', 'aria-pressed': on ? 'true' : 'false', text: opt });
      chip.addEventListener('click', function () {
        var list = (get(field.path) || []).slice();
        var idx = list.indexOf(opt);
        if (idx >= 0) list.splice(idx, 1);
        else {
          if (opt === field.noneOption) list = [];
          else if (field.noneOption) list = list.filter(function (x) { return x !== field.noneOption; });
          list.push(opt);
        }
        set(field.path, list);
        rerender(field.key);
      });
      group.appendChild(chip);
    });
    return fieldShell(field, group);
  }

  function renderSuggestText(field) {
    var id = 'f-' + field.key;
    var input = h('textarea', {
      id: id, maxlength: field.maxLength || 300, rows: 2, placeholder: field.placeholder || null,
      'aria-invalid': errors[field.key] ? 'true' : null, 'aria-describedby': describedBy(field), 'aria-required': 'true'
    });
    input.value = get(field.path) || '';
    input.addEventListener('input', function () { set(field.path, input.value); });
    var chips = h('div', { class: 'chips pt-suggest', 'aria-label': 'Gợi ý nhanh' });
    field.suggestions.forEach(function (s) {
      var chip = h('button', { type: 'button', class: 'chip', 'aria-pressed': containsPhrase(input.value, s) ? 'true' : 'false', text: s });
      chip.addEventListener('click', function () {
        var v = togglePhrase(input.value, s);
        input.value = v;
        set(field.path, v);
        chip.setAttribute('aria-pressed', containsPhrase(v, s) ? 'true' : 'false');
      });
      chips.appendChild(chip);
    });
    var wrap = fieldShell(field, input, id);
    wrap.appendChild(h('div', { class: 'hint', text: 'Chạm để thêm nhanh:' }));
    wrap.appendChild(chips);
    return wrap;
  }

  function renderOnset(field) {
    var id = 'f-onset';
    var num = h('input', { id: id, type: 'number', inputmode: 'numeric', min: 0, max: 1000, step: 1, value: state.answers.onsetValue,
      placeholder: 'Số', 'aria-invalid': errors[field.key] ? 'true' : null, 'aria-describedby': describedBy(field) });
    num.addEventListener('input', function () { set('answers.onsetValue', num.value); });
    var unit = h('select', { 'aria-label': 'Đơn vị thời gian' });
    ONSET_UNITS.forEach(function (u) {
      var o = h('option', { value: u, text: u + ' trước' });
      if (state.answers.onsetUnit === u) o.selected = true;
      unit.appendChild(o);
    });
    unit.addEventListener('change', function () { set('answers.onsetUnit', unit.value); });
    return fieldShell(field, h('div', { class: 'pt-onset' }, num, unit), id);
  }

  function renderDate3(field) {
    var p = state.patient;
    function box(key, placeholder, max, label, width) {
      var input = h('input', { type: 'text', inputmode: 'numeric', maxlength: width, placeholder: placeholder, value: p[key] || '',
        'aria-label': label, 'aria-invalid': errors[field.key] ? 'true' : null, 'aria-describedby': describedBy(field), autocomplete: 'off' });
      input.addEventListener('input', function () {
        input.value = input.value.replace(/\D/g, '').slice(0, width);
        p[key] = input.value;
        touched();
        if (input.value.length === width && key !== 'birthYear') {
          var next = input.parentNode.children[Array.prototype.indexOf.call(input.parentNode.children, input) + 1];
          if (next) next.focus();
        }
      });
      input.id = 'f-' + key;
      return input;
    }
    var grid = h('div', { class: 'pt-date3' },
      box('birthDay', 'Ngày', 31, 'Ngày sinh', 2),
      box('birthMonth', 'Tháng', 12, 'Tháng sinh', 2),
      box('birthYear', 'Năm (vd 1975)', 2026, 'Năm sinh', 4));
    return fieldShell(field, grid, 'f-birthDay');
  }

  function renderRange(field) {
    var value = state.answers.painScore;
    var display = h('span', { class: 'pt-range-value' + (value === null ? ' none' : '') });
    function describe(v) {
      if (v === null) return 'Chưa chọn — kéo thanh trượt';
      var n = Number(v);
      var word = n === 0 ? 'không đau' : n <= 3 ? 'nhẹ' : n <= 6 ? 'vừa' : n <= 8 ? 'nhiều' : 'rất nhiều';
      return n + '/10 · ' + word;
    }
    display.textContent = describe(value);
    var slider = h('input', { id: 'f-pain', type: 'range', min: 0, max: 10, step: 1, value: value === null ? 0 : value,
      'aria-valuetext': describe(value) });
    slider.addEventListener('input', function () {
      state.answers.painScore = Number(slider.value);
      display.textContent = describe(slider.value);
      display.className = 'pt-range-value';
      slider.setAttribute('aria-valuetext', describe(slider.value));
      touched();
    });
    var clear = h('button', { type: 'button', class: 'btn ghost small', text: 'Bỏ chọn' });
    clear.addEventListener('click', function () { state.answers.painScore = null; touched(); rerender(field.key); });
    var wrap = h('div', { class: 'pt-range' },
      h('div', { class: 'row between' }, display, value === null ? null : clear),
      slider,
      h('div', { class: 'pt-range-scale' }, h('span', { text: '0 · Không đau' }), h('span', { text: '5' }), h('span', { text: '10 · Đau dữ dội' })));
    return fieldShell(field, wrap, 'f-pain');
  }

  function renderDate(field) {
    var id = 'f-' + field.key;
    var input = h('input', { id: id, type: 'date', value: get(field.path) || '', max: new Date().toISOString().slice(0, 10) });
    input.addEventListener('input', function () { set(field.path, input.value); });
    return fieldShell(field, input, id);
  }

  function renderBody(field) {
    function num(key, suffix, label, max) {
      var input = h('input', { type: 'number', inputmode: 'decimal', min: 0, max: max, step: 'any', value: state.answers[key],
        'aria-label': label, 'aria-invalid': errors[field.key] ? 'true' : null });
      input.id = 'f-' + key;
      input.addEventListener('input', function () { state.answers[key] = input.value; touched(); });
      return h('div', { class: 'input-suffix' }, input, h('span', { text: suffix }));
    }
    return fieldShell(field, h('div', { class: 'grid-2 keep-2' }, num('weightKg', 'kg', 'Cân nặng', 300), num('heightCm', 'cm', 'Chiều cao', 250)), 'f-weightKg');
  }

  var RENDERERS = {
    text: renderText, tel: renderText, textarea: renderTextarea, choice: renderChoice, chips: renderChips,
    suggestText: renderSuggestText, onset: renderOnset, date3: renderDate3, range: renderRange, date: renderDate, body: renderBody
  };

  // ------------------------------------------------------------------ phrases / formatting
  function titleCase(value) {
    return (value || '').replace(/\s+/g, ' ').trim().split(' ').map(function (w) {
      return w ? w.charAt(0).toLocaleUpperCase('vi') + w.slice(1).toLocaleLowerCase('vi') : w;
    }).join(' ');
  }

  function fold(s) {
    return (s || '').toLocaleLowerCase('vi').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd');
  }

  function containsPhrase(text, phrase) {
    return fold(text).indexOf(fold(phrase)) >= 0;
  }

  function togglePhrase(text, phrase) {
    var parts = (text || '').split(/\s*[,;]\s*/).filter(function (p) { return p.trim(); });
    var idx = -1;
    parts.forEach(function (p, i) { if (fold(p) === fold(phrase)) idx = i; });
    if (idx >= 0) parts.splice(idx, 1);
    else parts.push(parts.length ? phrase.toLocaleLowerCase('vi') : phrase);
    var result = parts.join(', ');
    return result ? result.charAt(0).toLocaleUpperCase('vi') + result.slice(1) : '';
  }

  function birthText() {
    var p = state.patient;
    if (!p.birthYear) return '';
    if (p.birthDay && p.birthMonth) return pad(p.birthDay) + '/' + pad(p.birthMonth) + '/' + p.birthYear;
    return 'Năm ' + p.birthYear;
  }

  function pad(v) { v = String(v); return v.length < 2 ? '0' + v : v; }

  function displayValue(field) {
    if (field.type === 'date3') {
      var age = ageOf(state);
      return birthText() + (age !== null ? ' (' + age + ' tuổi)' : '');
    }
    if (field.type === 'onset') return state.answers.onsetValue !== '' ? state.answers.onsetValue + ' ' + state.answers.onsetUnit + ' trước' : '';
    if (field.type === 'range') return state.answers.painScore === null ? '' : state.answers.painScore + '/10';
    if (field.type === 'body') {
      var parts = [];
      if (state.answers.weightKg !== '') parts.push(state.answers.weightKg + ' kg');
      if (state.answers.heightCm !== '') parts.push(state.answers.heightCm + ' cm');
      return parts.join(' · ');
    }
    var v = get(field.path);
    if (Array.isArray(v)) return v.join(', ');
    if (field.type === 'choice') {
      var opt = field.options.filter(function (o) { return o[0] === v; })[0];
      return opt ? opt[1] : '';
    }
    if (field.type === 'date' && v) return v.split('-').reverse().join('/');
    return v || '';
  }

  // ------------------------------------------------------------------ views
  function emergencyNotice() {
    return h('div', { class: 'alert danger pt-emergency', role: 'note' },
      h('span', { class: 'icon', 'aria-hidden': 'true', text: '⚠' }),
      h('div', null,
        h('strong', { text: 'Dấu hiệu cấp cứu? Đừng chờ khai tờ khai.' }),
        'Đau ngực dữ dội, khó thở nặng, yếu liệt tay chân, nói khó, chảy máu nhiều, lơ mơ hoặc co giật: hãy báo ngay nhân viên y tế hoặc đến khoa Cấp cứu.'));
  }

  function progress() {
    var total = STEPS.length;
    var dots = h('div', { class: 'pt-dots', 'aria-hidden': 'true' });
    for (var i = 0; i < total; i++) dots.appendChild(h('span', { class: i < stepIndex ? 'done' : i === stepIndex ? 'current' : '' }));
    return h('div', { class: 'pt-progress' },
      h('div', { class: 'pt-progress-top' },
        h('h1', { class: 'pt-progress-title', id: 'stepTitle', tabindex: '-1', text: STEPS[stepIndex].title }),
        h('span', { class: 'pt-progress-step', text: 'Bước ' + (stepIndex + 1) + '/' + total })),
      dots);
  }

  function render(focusKey) {
    var step = STEPS[stepIndex];
    app.textContent = '';
    app.appendChild(progress());
    if (step.emergency && stepIndex === 0) app.appendChild(emergencyNotice());
    if (step.review) {
      renderReview();
    } else {
      var card = h('section', { class: 'pt-card', 'aria-labelledby': 'stepTitle' });
      if (step.intro) card.appendChild(h('p', { class: 'pt-intro', text: step.intro }));
      var fields = h('div', { class: 'pt-fields' });
      step.fields.forEach(function (field) {
        if (!isVisible(field)) return;
        fields.appendChild(RENDERERS[field.type](field));
      });
      card.appendChild(fields);
      app.appendChild(card);
      if (stepIndex === 1) app.appendChild(h('div', { class: 'pt-footer-note' }, emergencyNotice()));
    }
    app.appendChild(actionBar());
    if (focusKey) focusField(focusKey);
  }

  function rerender(focusKey) {
    var y = window.scrollY;
    render(null);
    window.scrollTo(0, y);
    if (focusKey) {
      var el = app.querySelector('[data-key="' + focusKey + '"] [aria-checked="true"], [data-key="' + focusKey + '"] [aria-pressed="true"], [data-key="' + focusKey + '"] input, [data-key="' + focusKey + '"] button');
      if (el) el.focus({ preventScroll: true });
    }
  }

  function focusField(key) {
    var wrap = app.querySelector('[data-key="' + key + '"]');
    if (!wrap) return;
    wrap.scrollIntoView({ behavior: 'smooth', block: 'center' });
    var control = wrap.querySelector('input, textarea, select, button');
    if (control) setTimeout(function () { control.focus({ preventScroll: true }); }, 250);
  }

  function actionBar() {
    var isReview = STEPS[stepIndex].review;
    var back = h('button', { type: 'button', class: 'btn back', text: stepIndex === 0 ? 'Thoát' : '← Quay lại' });
    back.addEventListener('click', function () {
      if (stepIndex === 0) { confirmRestart(); return; }
      errors = {};
      stepIndex--;
      touched();
      render();
      window.scrollTo(0, 0);
      document.getElementById('stepTitle').focus({ preventScroll: true });
    });
    var next = h('button', { type: 'button', class: 'btn primary', id: 'nextBtn', text: isReview ? (submitting ? 'Đang gửi…' : 'Gửi tờ khai') : 'Tiếp tục →' });
    if (submitting) next.disabled = true;
    next.addEventListener('click', isReview ? submit : goNext);
    return h('div', { class: 'pt-actions' }, h('div', { class: 'pt-actions-inner' }, back, next));
  }

  function goNext() {
    var first = validateStep(STEPS[stepIndex]);
    if (first) {
      render(first);
      toast('Vui lòng kiểm tra lại mục được đánh dấu đỏ.', 'error');
      return;
    }
    errors = {};
    stepIndex++;
    touched();
    render();
    window.scrollTo(0, 0);
    document.getElementById('stepTitle').focus({ preventScroll: true });
  }

  function renderReview() {
    STEPS.forEach(function (step, index) {
      if (step.review) return;
      var dl = h('dl', { class: 'kv' });
      var count = 0;
      step.fields.forEach(function (field) {
        if (!isVisible(field)) return;
        var v = displayValue(field);
        if (!v) return;
        count++;
        dl.appendChild(h('dt', { text: field.label }));
        dl.appendChild(h('dd', { text: v }));
      });
      var edit = h('button', { type: 'button', class: 'btn ghost small', text: 'Sửa' });
      edit.addEventListener('click', function () { stepIndex = index; errors = {}; render(); window.scrollTo(0, 0); });
      app.appendChild(h('section', { class: 'pt-card pt-review-section' },
        h('h3', null, h('span', { text: step.title }), edit),
        count ? dl : h('p', { class: 'muted', text: 'Không có thông tin.' })));
    });

    var consentId = 'f-consent';
    var consent = h('input', { type: 'checkbox', id: consentId, 'aria-invalid': errors.consent ? 'true' : null });
    consent.checked = !!state.consent;
    consent.addEventListener('change', function () { state.consent = consent.checked; touched(); });
    var consentBox = h('div', { class: 'pt-consent', 'data-key': 'consent' },
      h('label', { class: 'check', for: consentId }, consent,
        h('span', { text: 'Tôi xác nhận thông tin trên là đúng và đồng ý để ' + config.hospitalName +
          ' sử dụng thông tin này cho việc khám, chữa bệnh. Thông tin chỉ nhân viên y tế được xem và tự động xóa khỏi hệ thống tờ khai sau vài ngày.' })),
      errors.consent ? h('div', { class: 'error-text', role: 'alert', text: errors.consent }) : null);
    var honeypot = h('div', { class: 'hp-field', 'aria-hidden': 'true' },
      h('label', { for: 'f-website', text: 'Website' }), h('input', { id: 'f-website', type: 'text', tabindex: '-1', autocomplete: 'off' }));
    app.appendChild(h('section', { class: 'pt-card' },
      h('p', { class: 'muted', text: 'Sau khi gửi, bạn sẽ nhận một mã tờ khai. Hãy đưa mã này cho điều dưỡng hoặc quầy tiếp nhận.' }),
      consentBox, honeypot));
  }

  function payload() {
    var p = state.patient;
    var birthDate = p.birthYear;
    if (p.birthDay && p.birthMonth) birthDate = p.birthYear + '-' + pad(p.birthMonth) + '-' + pad(p.birthDay);
    var a = {};
    Object.keys(state.answers).forEach(function (k) {
      var v = state.answers[k];
      if (v === null || v === '' || (Array.isArray(v) && v.length === 0)) return;
      if (k === 'onsetValue' || k === 'weightKg' || k === 'heightCm') v = Number(v);
      a[k] = v;
    });
    if (!isFemaleOfChildbearingAge(state)) { delete a.pregnancy; delete a.lastMenstrualPeriod; }
    if (!allergyYes(state)) { delete a.allergyDrugs; delete a.allergyFoods; delete a.allergyOther; delete a.allergyReaction; }
    if (a.priorTreatment === 'Chưa điều trị') delete a.priorTreatmentDetail;
    var website = document.getElementById('f-website');
    return {
      formVersion: config.formVersion,
      consent: !!state.consent,
      website: website ? website.value : '',
      patient: {
        fullName: titleCase(p.fullName), birthDate: birthDate, gender: p.gender, phone: p.phone,
        nationalId: p.nationalId, hisPatientId: p.hisPatientId, filledBy: p.filledBy, relation: p.filledBy === 'relative' ? p.relation : ''
      },
      answers: a
    };
  }

  var ERROR_FIELDS = {
    invalid_name: ['patient', 'fullName'], invalid_gender: ['patient', 'gender'], invalid_phone: ['patient', 'phone'],
    invalid_national_id: ['patient', 'nationalId'], invalid_birth_date: ['patient', 'birth'], missing_complaint: ['complaint', 'chiefComplaint'],
    consent_required: ['review', 'consent']
  };

  function submit() {
    if (submitting) return;
    for (var i = 0; i < STEPS.length - 1; i++) {
      var first = validateStep(STEPS[i]);
      if (first) {
        stepIndex = i;
        render(first);
        toast('Còn mục chưa điền đúng ở bước "' + STEPS[i].title + '".', 'error');
        return;
      }
    }
    errors = {};
    if (!state.consent) {
      errors.consent = 'Cần xác nhận và đồng ý trước khi gửi.';
      render('consent');
      return;
    }
    submitting = true;
    render();
    fetch('/api/public/intakes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload()),
      credentials: 'same-origin'
    }).then(function (res) {
      return res.json().catch(function () { return {}; }).then(function (body) { return { ok: res.ok, status: res.status, body: body }; });
    }).then(function (r) {
      submitting = false;
      if (r.ok) {
        clearDraft();
        showDone(r.body.code);
        return;
      }
      var target = ERROR_FIELDS[r.body.error];
      if (target) {
        stepIndex = STEPS.findIndex(function (s) { return s.id === target[0]; });
        errors = {};
        errors[target[1]] = r.body.message;
        render(target[1]);
      } else {
        render();
      }
      toast(r.body.message || 'Không gửi được tờ khai (lỗi ' + r.status + ').', 'error');
    }).catch(function () {
      submitting = false;
      render();
      toast('Mất kết nối. Kiểm tra mạng rồi bấm "Gửi tờ khai" lần nữa — thông tin vẫn được giữ.', 'error');
    });
  }

  var doneTimer = null;
  function showDone(code) {
    app.textContent = '';
    restartBtn.hidden = true;
    var steps = h('ol', { class: 'pt-steps-list' },
      h('li', { text: 'Đưa mã này cho quầy tiếp nhận hoặc điều dưỡng khi đến khám (có thể chụp màn hình để lưu).' }),
      h('li', { text: 'Nhân viên y tế sẽ đối chiếu thông tin trước khi chuyển cho bác sĩ.' }),
      h('li', { text: 'Nếu triệu chứng nặng lên trong lúc chờ, hãy báo ngay nhân viên y tế.' }));
    var again = h('button', { type: 'button', class: 'btn primary large', text: 'Khai tờ khai mới' });
    again.addEventListener('click', resetAll);
    var card = h('section', { class: 'pt-card pt-done', 'aria-labelledby': 'doneTitle' },
      h('div', { class: 'pt-done-icon', 'aria-hidden': 'true', text: '✓' }),
      h('h1', { id: 'doneTitle', tabindex: '-1', text: 'Đã gửi tờ khai' }),
      h('p', { class: 'muted', text: 'Mã tờ khai của bạn' }),
      h('div', { class: 'pt-code', 'aria-label': 'Mã tờ khai ' + code.split('').join(' '), text: code }),
      steps,
      KIOSK ? h('div', { class: 'row' }, again) : null,
      KIOSK ? h('div', { class: 'pt-kiosk-timer', id: 'kioskTimer' }) : null);
    app.appendChild(card);
    document.getElementById('doneTitle').focus();
    window.scrollTo(0, 0);
    if (KIOSK) {
      var remaining = 60;
      var label = document.getElementById('kioskTimer');
      clearInterval(doneTimer);
      doneTimer = setInterval(function () {
        remaining--;
        label.textContent = 'Màn hình sẽ tự làm mới sau ' + remaining + ' giây để bảo mật thông tin.';
        if (remaining <= 0) resetAll();
      }, 1000);
    }
  }

  function resetAll() {
    clearInterval(doneTimer);
    clearDraft();
    state = emptyState();
    stepIndex = 0;
    errors = {};
    submitting = false;
    restartBtn.hidden = true;
    closeIdleModal();
    render();
    window.scrollTo(0, 0);
  }

  function confirmRestart() {
    if (!hasContent()) { resetAll(); return; }
    modal('Khai lại từ đầu?', 'Toàn bộ thông tin đã nhập trên máy này sẽ bị xóa.', 'Xóa và khai lại', resetAll);
  }

  function modal(title, text, confirmText, onConfirm, cancelText) {
    var backdrop = h('div', { class: 'modal-backdrop', role: 'presentation' });
    var cancel = h('button', { type: 'button', class: 'btn', text: cancelText || 'Không' });
    var ok = h('button', { type: 'button', class: 'btn primary', text: confirmText });
    var box = h('div', { class: 'modal', role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': 'modalTitle' },
      h('h2', { id: 'modalTitle', text: title }), h('p', { text: text }), h('div', { class: 'modal-actions' }, cancel, ok));
    backdrop.appendChild(box);
    function close() { backdrop.remove(); }
    cancel.addEventListener('click', close);
    ok.addEventListener('click', function () { close(); onConfirm(); });
    backdrop.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });
    document.body.appendChild(backdrop);
    ok.focus();
    return { close: close, box: box };
  }

  // ------------------------------------------------------------------ kiosk idle handling
  function closeIdleModal() {
    if (idleModal) { idleModal.close(); idleModal = null; }
  }

  function startKioskIdleWatch() {
    ['pointerdown', 'keydown', 'input', 'scroll', 'touchstart'].forEach(function (evt) {
      document.addEventListener(evt, function () { lastActivity = Date.now(); }, { passive: true });
    });
    setInterval(function () {
      if (!hasContent() || idleModal || doneTimer) return;
      if (Date.now() - lastActivity > 150000) {
        var remaining = 30;
        idleModal = modal('Bạn vẫn đang khai chứ?', 'Để bảo mật, tờ khai sẽ tự xóa sau 30 giây nếu không có thao tác.', 'Tôi vẫn đang khai', function () {
          idleModal = null;
          lastActivity = Date.now();
        }, 'Xóa ngay');
        var cancelBtn = idleModal.box.querySelector('.btn:not(.primary)');
        cancelBtn.addEventListener('click', resetAll);
        var p = idleModal.box.querySelector('p');
        var timer = setInterval(function () {
          if (!idleModal) { clearInterval(timer); return; }
          remaining--;
          p.textContent = 'Để bảo mật, tờ khai sẽ tự xóa sau ' + remaining + ' giây nếu không có thao tác.';
          if (remaining <= 0) { clearInterval(timer); resetAll(); }
        }, 1000);
      }
    }, 5000);
  }

  // ------------------------------------------------------------------ boot
  restartBtn.addEventListener('click', confirmRestart);

  function start() {
    var draft = loadDraft();
    if (draft && draft.state && (draft.state.patient.fullName || draft.state.answers.chiefComplaint)) {
      app.textContent = '';
      var resume = h('button', { type: 'button', class: 'btn primary large block', text: 'Tiếp tục khai' });
      var fresh = h('button', { type: 'button', class: 'btn large block', text: 'Khai mới từ đầu' });
      resume.addEventListener('click', function () {
        state = draft.state;
        stepIndex = Math.min(draft.step || 0, STEPS.length - 1);
        restartBtn.hidden = false;
        render();
      });
      fresh.addEventListener('click', resetAll);
      app.appendChild(h('section', { class: 'pt-card stack' },
        h('h1', { text: 'Tiếp tục tờ khai đang dở?' }),
        h('p', { class: 'muted', text: 'Trên trình duyệt này còn một tờ khai chưa gửi.' }), resume, fresh));
      return;
    }
    render();
  }

  fetch('/api/public/config', { credentials: 'same-origin' })
    .then(function (r) { return r.ok ? r.json() : {}; })
    .then(function (c) {
      if (c && c.hospitalName) config = c;
      document.getElementById('hospitalName').textContent = config.hospitalName + (config.departmentName ? ' · ' + config.departmentName : '');
      document.title = 'Tờ khai trước khám · ' + config.hospitalName;
    })
    .catch(function () { /* offline: keep defaults */ })
    .then(function () {
      start();
      if (KIOSK) startKioskIdleWatch();
    });
})();
