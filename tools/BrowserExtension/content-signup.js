(function () {
  "use strict";

  // 1. Guard against Vietnam IP blackholed routes (/, /login, /Login, /CreateAccount, /home)
  var path = (window.location.pathname || "/").toLowerCase();
  if (path === "/" || path === "/login" || path === "/home") {
    window.location.replace("https://www.roblox.com/NewLogin" + (window.location.hash || ""));
    return;
  }
  if (path === "/createaccount" || path === "/account/signupredir") {
    window.location.replace("https://www.roblox.com/NewLogin#signup");
    return;
  }

  if (path !== "/newlogin") {
    return;
  }

  function mountNewLoginEnhancements() {
    if (!document.body || !document.head) {
      setTimeout(mountNewLoginEnhancements, 60);
      return;
    }

    var contentEl = document.getElementById("content") || document.body;
    if (!contentEl) {
      setTimeout(mountNewLoginEnhancements, 60);
      return;
    }

    ensureStylesMounted();
    ensureTopModeBarMounted(contentEl);
    ensureSignUpCardMounted(contentEl);
    syncModeFromHash();
  }

  function ensureStylesMounted() {
    if (document.getElementById("rbx-mgr-signup-style")) {
      return;
    }

    var style = document.createElement("style");
    style.id = "rbx-mgr-signup-style";
    style.textContent = [
      "body.rbx-mgr-signup-mode #react-login-web-app, body.rbx-mgr-signup-mode #react-login-container, body.rbx-mgr-signup-mode #login-container { display: none !important; }",
      "#rbx-mgr-top-bar { max-width: 430px; margin: 22px auto 0 auto; padding: 10px 14px; border-radius: 10px; background: #18191c; border: 1px solid #393b3d; display: flex; flex-direction: column; gap: 8px; font-family: 'Builder Sans', 'Segoe UI', Arial, sans-serif; position: relative; z-index: 20; }",
      "#rbx-mgr-top-tabs { display: flex; gap: 8px; }",
      ".rbx-mgr-tab-btn { flex: 1; padding: 9px 12px; border-radius: 8px; border: 1px solid #3f4145; background: #111214; color: #d4d4d8; font-size: 13px; font-weight: 700; cursor: pointer; text-align: center; transition: all 0.15s ease; }",
      ".rbx-mgr-tab-btn.active { background: #2563eb; border-color: #3b82f6; color: #ffffff; }",
      "#rbx-mgr-imported-banner { display: none; padding: 8px 12px; border-radius: 8px; background: rgba(16, 185, 129, 0.15); border: 1px solid rgba(52, 211, 153, 0.45); color: #34d399; font-size: 12.5px; font-weight: 600; text-align: center; }",
      "#rbx-mgr-signup-root { display: none; background: #232527; color: #ffffff; max-width: 430px; margin: 14px auto 42px auto; padding: 26px 24px; border-radius: 12px; border: 1px solid #393b3d; box-shadow: 0 12px 36px rgba(0, 0, 0, 0.55); font-family: 'Builder Sans', 'Segoe UI', Arial, sans-serif; position: relative; z-index: 15; }",
      "body.rbx-mgr-signup-mode #rbx-mgr-signup-root { display: block !important; }",
      "#rbx-mgr-signup-root h2 { margin: 0 0 6px 0; font-size: 22px; font-weight: 800; text-align: center; color: #fff; }",
      "#rbx-mgr-signup-root .rbx-su-sub { text-align: center; font-size: 12.5px; color: #9ca3af; margin-bottom: 16px; }",
      "#rbx-mgr-signup-root label { display: block; font-size: 13px; font-weight: 600; color: #d4d4d8; margin: 12px 0 6px 0; }",
      "#rbx-mgr-signup-root input, #rbx-mgr-signup-root select { width: 100%; box-sizing: border-box; background: #111214; border: 1px solid #4b4d50; color: #fff; border-radius: 8px; padding: 10px 12px; font-size: 14px; outline: none; }",
      "#rbx-mgr-signup-root input:focus, #rbx-mgr-signup-root select:focus { border-color: #3b82f6; }",
      "#rbx-mgr-signup-root .rbx-su-row { display: flex; gap: 8px; }",
      "#rbx-mgr-signup-root .rbx-su-msg { font-size: 12px; min-height: 16px; margin-top: 4px; }",
      "#rbx-mgr-signup-root .rbx-su-gender-btn { flex: 1; background: #111214; border: 1px solid #4b4d50; color: #d4d4d8; padding: 9px; border-radius: 8px; cursor: pointer; font-weight: 600; font-size: 13px; }",
      "#rbx-mgr-signup-root .rbx-su-gender-btn.active { border-color: #3b82f6; background: #1e293b; color: #60a5fa; }",
      "#rbx-su-submit { width: 100%; margin-top: 18px; background: #2563eb; color: #ffffff; border: none; border-radius: 8px; padding: 12px; font-size: 15px; font-weight: 700; cursor: pointer; }",
      "#rbx-su-submit:hover { background: #1d4ed8; }",
      "#rbx-su-submit:disabled { opacity: 0.6; cursor: wait; }"
    ].join("\n");
    document.head.appendChild(style);
  }

  function ensureTopModeBarMounted(contentEl) {
    if (document.getElementById("rbx-mgr-top-bar")) {
      return;
    }

    var bar = document.createElement("div");
    bar.id = "rbx-mgr-top-bar";
    bar.innerHTML = [
      '<div id="rbx-mgr-imported-banner"></div>',
      '<div id="rbx-mgr-top-tabs">',
      '  <button type="button" id="rbx-mgr-tab-login" class="rbx-mgr-tab-btn">Đăng nhập (Login)</button>',
      '  <button type="button" id="rbx-mgr-tab-signup" class="rbx-mgr-tab-btn">Đăng ký (Sign Up - IP VN)</button>',
      '</div>'
    ].join("");

    contentEl.insertBefore(bar, contentEl.firstChild);

    document.getElementById("rbx-mgr-tab-login").onclick = function () {
      window.location.hash = "";
      syncModeFromHash();
    };
    document.getElementById("rbx-mgr-tab-signup").onclick = function () {
      window.location.hash = "#signup";
      syncModeFromHash();
    };
  }

  function ensureSignUpCardMounted(contentEl) {
    if (document.getElementById("rbx-mgr-signup-root")) {
      return;
    }

    var root = document.createElement("div");
    root.id = "rbx-mgr-signup-root";

    var dayOpts = "";
    for (var d = 1; d <= 31; d++) {
      dayOpts += '<option value="' + d + '"' + (d === 15 ? " selected" : "") + ">Ngày " + d + "</option>";
    }
    var monthOpts = "";
    for (var m = 1; m <= 12; m++) {
      monthOpts += '<option value="' + m + '"' + (m === 6 ? " selected" : "") + ">Tháng " + m + "</option>";
    }
    var yearOpts = "";
    for (var y = 2018; y >= 1960; y--) {
      yearOpts += '<option value="' + y + '"' + (y === 2001 ? " selected" : "") + ">" + y + "</option>";
    }

    root.innerHTML = [
      "<h2>Đăng ký Roblox (IP Việt Nam)</h2>",
      '<div class="rbx-su-sub">Tạo tài khoản trực tiếp trên trình duyệt thật &amp; IP Việt Nam (Chống khóa Account Locked)</div>',
      "<label>Ngày sinh (Birthday)</label>",
      '<div class="rbx-su-row">',
      '  <select id="rbx-su-day">' + dayOpts + "</select>",
      '  <select id="rbx-su-month">' + monthOpts + "</select>",
      '  <select id="rbx-su-year">' + yearOpts + "</select>",
      "</div>",
      "<label>Tên người dùng (Username)</label>",
      '<input id="rbx-su-username" type="text" placeholder="Từ 3-20 ký tự, không dùng dấu cách" maxlength="20" autocomplete="off" />',
      '<div id="rbx-su-username-msg" class="rbx-su-msg"></div>',
      "<label>Mật khẩu (Password)</label>",
      '<input id="rbx-su-password" type="password" placeholder="Tối thiểu 8 ký tự" autocomplete="new-password" />',
      '<div id="rbx-su-password-msg" class="rbx-su-msg"></div>',
      "<label>Giới tính (Tùy chọn)</label>",
      '<div class="rbx-su-row">',
      '  <button type="button" id="rbx-su-gender-f" class="rbx-su-gender-btn">♀ Nữ (Female)</button>',
      '  <button type="button" id="rbx-su-gender-m" class="rbx-su-gender-btn active">♂ Nam (Male)</button>',
      "</div>",
      '<div id="rbx-su-status" style="margin-top:12px;font-size:13px;text-align:center;min-height:18px;color:#fbbf24;"></div>',
      '<button type="button" id="rbx-su-submit">Đăng ký (Sign Up)</button>'
    ].join("");

    var topBar = document.getElementById("rbx-mgr-top-bar");
    if (topBar && topBar.nextSibling) {
      contentEl.insertBefore(root, topBar.nextSibling);
    } else {
      contentEl.appendChild(root);
    }

    var selectedGender = 2;
    var btnF = document.getElementById("rbx-su-gender-f");
    var btnM = document.getElementById("rbx-su-gender-m");
    btnF.onclick = function () {
      selectedGender = 3;
      btnF.classList.add("active");
      btnM.classList.remove("active");
    };
    btnM.onclick = function () {
      selectedGender = 2;
      btnM.classList.add("active");
      btnF.classList.remove("active");
    };

    function getBirthdayIso() {
      var y = document.getElementById("rbx-su-year").value;
      var m = ("0" + document.getElementById("rbx-su-month").value).slice(-2);
      var d = ("0" + document.getElementById("rbx-su-day").value).slice(-2);
      return y + "-" + m + "-" + d + "T00:00:00.000Z";
    }

    var uInput = document.getElementById("rbx-su-username");
    var uMsg = document.getElementById("rbx-su-username-msg");
    var uTimer = null;
    uInput.oninput = function () {
      clearTimeout(uTimer);
      var val = uInput.value.trim();
      if (val.length < 3) {
        uMsg.style.color = "#f87171";
        uMsg.textContent = "Tên người dùng cần ít nhất 3 ký tự.";
        return;
      }
      uMsg.style.color = "#9ca3af";
      uMsg.textContent = "Đang kiểm tra tên người dùng...";
      uTimer = setTimeout(function () {
        if (!window.CoreUtilities || !window.CoreUtilities.httpService) {
          return;
        }
        window.CoreUtilities.httpService
          .get(
            { url: "https://auth.roblox.com/v1/usernames/validate", withCredentials: true },
            { username: val, birthday: getBirthdayIso(), context: "Signup" }
          )
          .then(function (r) {
            if (r && r.data && r.data.code === 0) {
              uMsg.style.color = "#4ade80";
              uMsg.textContent = "✓ Tên người dùng hợp lệ!";
            } else {
              uMsg.style.color = "#f87171";
              uMsg.textContent = (r && r.data && r.data.message) || "Tên người dùng không hợp lệ.";
            }
          })
          .catch(function () {});
      }, 750);
    };

    var pInput = document.getElementById("rbx-su-password");
    var pMsg = document.getElementById("rbx-su-password-msg");
    var pTimer = null;
    pInput.oninput = function () {
      clearTimeout(pTimer);
      var val = pInput.value;
      if (val.length < 8) {
        pMsg.style.color = "#f87171";
        pMsg.textContent = "Mật khẩu cần ít nhất 8 ký tự.";
        return;
      }
      pTimer = setTimeout(function () {
        if (!window.CoreUtilities || !window.CoreUtilities.httpService) {
          return;
        }
        window.CoreUtilities.httpService
          .post(
            { url: "https://auth.roblox.com/v2/passwords/validate", withCredentials: true },
            { username: uInput.value.trim() || "RobloxUser", password: val }
          )
          .then(function (r) {
            if (r && r.data && r.data.code === 0) {
              pMsg.style.color = "#4ade80";
              pMsg.textContent = "✓ Mật khẩu hợp lệ!";
            } else {
              pMsg.style.color = "#f87171";
              pMsg.textContent = (r && r.data && r.data.message) || "Mật khẩu chưa đủ mạnh.";
            }
          })
          .catch(function () {});
      }, 750);
    };

    var submitBtn = document.getElementById("rbx-su-submit");
    var statusEl = document.getElementById("rbx-su-status");
    submitBtn.onclick = function () {
      var username = uInput.value.trim();
      var password = pInput.value;
      if (username.length < 3) {
        statusEl.style.color = "#f87171";
        statusEl.textContent = "Vui lòng nhập tên người dùng hợp lệ (3-20 ký tự).";
        return;
      }
      if (password.length < 8) {
        statusEl.style.color = "#f87171";
        statusEl.textContent = "Vui lòng nhập mật khẩu ít nhất 8 ký tự.";
        return;
      }
      if (!window.CoreUtilities || !window.CoreUtilities.httpService) {
        statusEl.style.color = "#fbbf24";
        statusEl.textContent = "Đang khởi tạo dịch vụ bảo mật Roblox, vui lòng thử lại sau 2 giây...";
        return;
      }

      submitBtn.disabled = true;
      statusEl.style.color = "#fbbf24";
      statusEl.textContent = "Đang gửi yêu cầu đăng ký (Hãy giải CAPTCHA nếu xuất hiện)...";

      var defaultAgreements = [
        "bcd1bb0e-5a98-4eb6-97e0-39170f27d032",
        "5c8b04b1-0890-4fa3-8c95-355808945530"
      ];

      window.CoreUtilities.httpService
        .get({
          url: "https://apis.roblox.com/user-agreements/v1/agreements-resolution/web",
          withCredentials: true
        })
        .then(function (agRes) {
          if (agRes && Array.isArray(agRes.data) && agRes.data.length > 0) {
            return agRes.data.map(function (x) {
              return x.id;
            });
          }
          return defaultAgreements;
        })
        .catch(function () {
          return defaultAgreements;
        })
        .then(function (agreementIds) {
          return window.CoreUtilities.httpService.post(
            { url: "https://auth.roblox.com/v2/signup", withCredentials: true },
            {
              username: username,
              password: password,
              birthday: getBirthdayIso(),
              gender: selectedGender,
              isTosAgreementBoxChecked: true,
              agreementIds: agreementIds
            }
          );
        })
        .then(function () {
          submitBtn.disabled = false;
          statusEl.style.color = "#4ade80";
          statusEl.textContent =
            "🎉 Đăng ký thành công! Cookie .ROBLOSECURITY đang được tự động chuyển vào RbxManager...";
        })
        .catch(function (err) {
          submitBtn.disabled = false;
          var errMsg = "Đăng ký chưa hoàn tất hoặc CAPTCHA bị hủy. Vui lòng thử lại.";
          if (err && err.data && Array.isArray(err.data.errors) && err.data.errors[0]) {
            errMsg = err.data.errors[0].message || errMsg;
          }
          statusEl.style.color = "#f87171";
          statusEl.textContent = errMsg;
        });
    };
  }

  function syncModeFromHash() {
    var hash = window.location.hash || "";
    var isSignUp = hash.toLowerCase().indexOf("#signup") === 0;
    var loginTab = document.getElementById("rbx-mgr-tab-login");
    var signUpTab = document.getElementById("rbx-mgr-tab-signup");
    var banner = document.getElementById("rbx-mgr-imported-banner");

    if (hash.indexOf("#imported=") === 0 && banner) {
      var importedUser = decodeURIComponent(hash.substring("#imported=".length));
      banner.style.display = "block";
      banner.textContent = "✓ Đã nhập tài khoản \"" + importedUser + "\" vào RbxManager thành công! Sẵn sàng cho tài khoản tiếp theo.";
    }

    if (isSignUp) {
      document.body.classList.add("rbx-mgr-signup-mode");
      if (signUpTab) signUpTab.classList.add("active");
      if (loginTab) loginTab.classList.remove("active");
    } else {
      document.body.classList.remove("rbx-mgr-signup-mode");
      if (loginTab) loginTab.classList.add("active");
      if (signUpTab) signUpTab.classList.remove("active");
    }
  }

  window.addEventListener("hashchange", syncModeFromHash);

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", mountNewLoginEnhancements);
  } else {
    mountNewLoginEnhancements();
  }
})();
