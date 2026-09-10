/*
 * Soạn nội dung chuyển vào HIS từ câu trả lời của người bệnh.
 * Kết quả là bản nháp: điều dưỡng xem, sửa rồi mới duyệt; bác sĩ kiểm tra lại trên HIS trước khi lưu.
 * Khóa (key) trùng với Field Key trong profile XML của HIS Assistant.
 */
(function (global) {
  'use strict';

  function str(v) { return v === null || v === undefined ? '' : String(v).trim(); }
  function list(v) { return Array.isArray(v) ? v.filter(function (x) { return str(x); }) : []; }
  function capFirst(s) { s = str(s); return s ? s.charAt(0).toLocaleUpperCase('vi') + s.slice(1) : ''; }
  function lowFirst(s) {
    s = str(s);
    if (!s) return '';
    // Keep acronyms (COPD, HIV...) and proper nouns written in capitals.
    if (s.length > 1 && s.charAt(1) === s.charAt(1).toLocaleUpperCase('vi') && /[A-ZÀ-Ỹ]/.test(s.charAt(1))) return s;
    return s.charAt(0).toLocaleLowerCase('vi') + s.slice(1);
  }
  function sentence(s) {
    s = str(s);
    if (!s) return '';
    s = capFirst(s);
    return /[.!?…]$/.test(s) ? s : s + '.';
  }
  function joinList(items) { return items.map(lowFirst).join(', '); }

  function onsetPhrase(a) {
    var v = str(a.onsetValue);
    if (!v) return '';
    return 'cách đây ' + v + ' ' + (str(a.onsetUnit) || 'ngày');
  }

  function composeHistory(record) {
    var a = record.answers || {};
    var p = record.patient || {};
    var parts = [];
    var chief = str(a.chiefComplaint);
    var onset = onsetPhrase(a);
    if (onset && chief) parts.push('Bệnh khởi phát ' + onset + ' với ' + lowFirst(chief) + '.');
    else if (chief) parts.push(sentence(chief));
    else if (onset) parts.push('Bệnh khởi phát ' + onset + '.');
    if (str(a.symptomDescription)) parts.push(sentence(a.symptomDescription));
    if (a.painScore !== undefined && a.painScore !== null && str(a.painScore) !== '') {
      parts.push(Number(a.painScore) === 0 ? 'Không đau.' : 'Mức độ đau/khó chịu ' + a.painScore + '/10.');
    }
    var assoc = list(a.associatedSymptoms);
    if (str(a.associatedOther)) assoc.push(str(a.associatedOther));
    if (assoc.length) parts.push('Kèm theo: ' + joinList(assoc) + '.');
    switch (str(a.priorTreatment)) {
      case 'Chưa điều trị':
        parts.push('Chưa điều trị gì trước khi đến khám.');
        break;
      case 'Tự mua thuốc':
        parts.push('Đã tự dùng thuốc' + (str(a.priorTreatmentDetail) ? ': ' + lowFirst(a.priorTreatmentDetail).replace(/\.$/, '') : '') + '.');
        break;
      case 'Đã khám nơi khác':
        parts.push('Đã khám/điều trị tại cơ sở khác' + (str(a.priorTreatmentDetail) ? ': ' + lowFirst(a.priorTreatmentDetail).replace(/\.$/, '') : '') + '.');
        break;
    }
    if (str(a.notes)) parts.push('Người bệnh ghi chú thêm: ' + lowFirst(a.notes).replace(/\.$/, '') + '.');
    if (p.filledBy === 'relative') parts.push('(Thông tin do người nhà' + (str(p.relation) ? ' – ' + str(p.relation) : '') + ' khai.)');
    return parts.join(' ');
  }

  function composePastHistory(record) {
    var a = record.answers || {};
    var p = record.patient || {};
    var lines = [];
    var chronic = list(a.chronicConditions).filter(function (x) { return x !== 'Không có bệnh nào'; });
    if (str(a.chronicOther)) chronic.push(str(a.chronicOther));
    if (chronic.length) lines.push('- Bệnh lý: ' + joinList(chronic) + '.');
    else if (list(a.chronicConditions).indexOf('Không có bệnh nào') >= 0) lines.push('- Bệnh lý: chưa ghi nhận bệnh mạn tính.');
    if (str(a.surgeries)) lines.push('- Phẫu thuật/thủ thuật: ' + lowFirst(a.surgeries).replace(/\.$/, '') + '.');
    if (str(a.medications)) lines.push('- Thuốc đang dùng: ' + lowFirst(a.medications).replace(/\.$/, '') + '.');
    var habits = [];
    if (a.smoking === 'Đang hút') habits.push('đang hút thuốc lá');
    else if (a.smoking === 'Đã bỏ') habits.push('đã bỏ thuốc lá');
    else if (a.smoking === 'Không') habits.push('không hút thuốc lá');
    if (a.alcohol === 'Thường xuyên') habits.push('uống rượu bia thường xuyên');
    else if (a.alcohol === 'Thỉnh thoảng') habits.push('thỉnh thoảng uống rượu bia');
    else if (a.alcohol === 'Không') habits.push('không uống rượu bia');
    if (habits.length) lines.push('- Thói quen: ' + habits.join('; ') + '.');
    if (p.gender === 'female' && (str(a.pregnancy) || str(a.lastMenstrualPeriod))) {
      var ob = [];
      if (a.pregnancy === 'Có') ob.push('đang mang thai');
      else if (a.pregnancy === 'Không chắc') ob.push('không chắc có thai');
      else if (a.pregnancy === 'Không') ob.push('không mang thai');
      if (str(a.lastMenstrualPeriod)) ob.push('kinh chót ngày ' + str(a.lastMenstrualPeriod).split('-').reverse().join('/'));
      lines.push('- Sản phụ khoa: ' + ob.join('; ') + '.');
    }
    return lines.length ? lines.join('\n') : 'Chưa ghi nhận bất thường.';
  }

  function composeFamily(record) {
    var a = record.answers || {};
    var items = list(a.familyConditions).filter(function (x) { return x !== 'Không có / không rõ'; });
    if (str(a.familyOther)) items.push(str(a.familyOther));
    if (items.length) return capFirst(joinList(items)) + '.';
    return 'Chưa ghi nhận bệnh lý liên quan.';
  }

  function composeAllergy(record) {
    var a = record.answers || {};
    if (a.allergyStatus === 'Không') return 'Chưa ghi nhận tiền sử dị ứng.';
    if (a.allergyStatus === 'Không rõ') return 'Không rõ tiền sử dị ứng.';
    if (a.allergyStatus !== 'Có') return '';
    var parts = [];
    if (str(a.allergyDrugs)) parts.push('Dị ứng thuốc: ' + str(a.allergyDrugs));
    if (str(a.allergyFoods)) parts.push('Dị ứng thức ăn: ' + str(a.allergyFoods));
    if (str(a.allergyOther)) parts.push('Dị ứng khác: ' + str(a.allergyOther));
    if (str(a.allergyReaction)) parts.push('Biểu hiện: ' + lowFirst(a.allergyReaction));
    return parts.length ? parts.join('. ') + '.' : 'Có tiền sử dị ứng (chưa rõ tác nhân).';
  }

  function composeSymptoms(record) {
    var a = record.answers || {};
    var items = [];
    if (str(a.chiefComplaint)) items.push(str(a.chiefComplaint));
    list(a.associatedSymptoms).forEach(function (s) { items.push(s); });
    if (str(a.associatedOther)) items.push(str(a.associatedOther));
    return items.length ? capFirst(joinList(items)) + '.' : '';
  }

  function num(v) {
    var s = str(v).replace(',', '.');
    if (!s) return '';
    var n = Number(s);
    return isFinite(n) ? String(Math.round(n * 10) / 10) : '';
  }

  /** Returns { key: text } using the HIS assistant profile Field keys. */
  function composeHisFields(record) {
    var a = record.answers || {};
    return {
      ReasonForAdmission: capFirst(str(a.chiefComplaint)).replace(/\.$/, ''),
      History: composeHistory(record),
      PastHistory: composePastHistory(record),
      FamilyHistory: composeFamily(record),
      Allergy: composeAllergy(record),
      Symptoms: composeSymptoms(record),
      Weight: num(a.weightKg),
      Height: num(a.heightCm)
    };
  }

  /** Text fields shown to the nurse, in HIS order. `rows` = textarea height hint. */
  var TEXT_FIELDS = [
    { key: 'ReasonForAdmission', label: 'Lý do khám / vào viện', rows: 2 },
    { key: 'History', label: 'Quá trình bệnh lý (bệnh sử)', rows: 6 },
    { key: 'PastHistory', label: 'Tiền sử bản thân', rows: 5 },
    { key: 'FamilyHistory', label: 'Tiền sử gia đình', rows: 2 },
    { key: 'Allergy', label: 'Dị ứng', rows: 2 },
    { key: 'Symptoms', label: 'Triệu chứng (màn hình Khám bệnh)', rows: 2 }
  ];

  /** Vital signs measured by the nurse (key, label, unit, validation range, placeholder). */
  var VITAL_FIELDS = [
    { key: 'Pulse', label: 'Mạch', unit: 'lần/phút', min: 20, max: 250, placeholder: '80' },
    { key: 'Temperature', label: 'Nhiệt độ', unit: '°C', min: 30, max: 45, placeholder: '37' },
    { key: 'BloodPressure', label: 'Huyết áp', unit: 'mmHg', pattern: /^\d{2,3}\/\d{2,3}$/, placeholder: '120/80' },
    { key: 'RespiratoryRate', label: 'Nhịp thở', unit: 'lần/phút', min: 5, max: 80, placeholder: '18' },
    { key: 'SpO2', label: 'SpO₂', unit: '%', min: 50, max: 100, placeholder: '98' },
    { key: 'Weight', label: 'Cân nặng', unit: 'kg', min: 1, max: 300, placeholder: '60' },
    { key: 'Height', label: 'Chiều cao', unit: 'cm', min: 30, max: 250, placeholder: '165' }
  ];

  function validateVital(field, value) {
    var v = str(value).replace(',', '.');
    if (!v) return null;
    if (field.pattern) return field.pattern.test(v) ? null : field.label + ' nhập dạng ' + field.placeholder;
    var n = Number(v);
    if (!isFinite(n) || n < field.min || n > field.max) return field.label + ' ngoài khoảng hợp lý (' + field.min + '–' + field.max + ' ' + field.unit + ')';
    return null;
  }

  function bmi(weight, height) {
    var w = Number(str(weight).replace(',', '.')), h = Number(str(height).replace(',', '.')) / 100;
    if (!w || !h) return '';
    var value = w / (h * h);
    return isFinite(value) && value > 5 && value < 90 ? (Math.round(value * 10) / 10).toString() : '';
  }

  global.IntakeCompose = {
    composeHisFields: composeHisFields,
    TEXT_FIELDS: TEXT_FIELDS,
    VITAL_FIELDS: VITAL_FIELDS,
    validateVital: validateVital,
    bmi: bmi
  };
})(typeof window !== 'undefined' ? window : this);
