const BRIDGE_ENDPOINT = "http://127.0.0.1:48123/import";
const SNI_PROXY_HOST = "127.0.0.1:48124";
const ROBLOX_COOKIE_URL = "https://www.roblox.com/";
const ROBLOX_LOGIN_URL = "https://www.roblox.com/NewLogin";
const COOKIE_NAME = ".ROBLOSECURITY";

let importInFlight = false;
let lastImportedCookieValue = "";

function applyVietnamSniBypassProxy() {
  if (!chrome.proxy || !chrome.proxy.settings) {
    return;
  }

  const pacScript = `
    function FindProxyForURL(url, host) {
      var h = (host || "").toLowerCase();
      if (h === "www.roblox.com" || h === "roblox.com" || h.endsWith(".roblox.com")) {
        return "PROXY ${SNI_PROXY_HOST}; DIRECT";
      }
      return "DIRECT";
    }
  `;

  chrome.proxy.settings.set(
    {
      value: {
        mode: "pac_script",
        pacScript: {
          data: pacScript
        }
      },
      scope: "regular"
    },
    () => {
      void chrome.runtime.lastError;
    }
  );
}

applyVietnamSniBypassProxy();

chrome.runtime.onInstalled.addListener(() => {
  applyVietnamSniBypassProxy();
});

chrome.runtime.onStartup.addListener(() => {
  applyVietnamSniBypassProxy();
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message && message.type === "IMPORT_ROBLOSECURITY") {
    handleImportRequest()
      .then(sendResponse)
      .catch((err) => {
        sendResponse({
          ok: false,
          error: (err && err.message) ? err.message : "Unexpected extension error."
        });
      });
    return true;
  }
});

// Automatically import newly created/logged-in .ROBLOSECURITY cookies before /home can time out on Vietnam IPs
chrome.cookies.onChanged.addListener((changeInfo) => {
  if (!changeInfo || changeInfo.removed || !changeInfo.cookie) {
    return;
  }

  const cookie = changeInfo.cookie;
  if (cookie.name !== COOKIE_NAME || !cookie.value || cookie.value === lastImportedCookieValue) {
    return;
  }

  if (!cookie.domain || !cookie.domain.toLowerCase().endsWith("roblox.com")) {
    return;
  }

  void handleImportRequest({ silentIfBridgeDown: true });
});

async function handleImportRequest(options = {}) {
  if (importInFlight) {
    return {
      ok: false,
      error: "Import already in progress."
    };
  }

  importInFlight = true;
  try {
    const cookie = await chrome.cookies.get({
      url: ROBLOX_COOKIE_URL,
      name: COOKIE_NAME
    });

    if (!cookie || !cookie.value) {
      return {
        ok: false,
        error: "No active .ROBLOSECURITY cookie found. Log in to roblox.com first."
      };
    }

    let response;
    try {
      response = await fetch(BRIDGE_ENDPOINT, {
        method: "POST",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ cookie: cookie.value })
      });
    } catch {
      return {
        ok: false,
        error: "Cannot connect to RbxManager (127.0.0.1:48123). Open RbxManager first."
      };
    }

    let payload = {};
    try {
      payload = await response.json();
    } catch {
      // Ignore non-JSON body
    }

    if (!response.ok || !payload.ok) {
      return {
        ok: false,
        error: payload.error || `RbxManager rejected import (HTTP ${response.status}).`
      };
    }

    lastImportedCookieValue = cookie.value;

    // Remove ONLY .ROBLOSECURITY so anti-bot/session cookies and IndexedDB hardware keys remain intact
    // and the server-side token is not invalidated by a logout request.
    const removeDetails = {
      url: ROBLOX_COOKIE_URL,
      name: COOKIE_NAME
    };
    if (cookie.storeId) {
      removeDetails.storeId = cookie.storeId;
    }
    await chrome.cookies.remove(removeDetails);

    const importedUsername = payload.username || "Roblox Account";
    const nextUrl = `${ROBLOX_LOGIN_URL}#imported=${encodeURIComponent(importedUsername)}`;

    const activeRobloxTabs = await chrome.tabs.query({
      active: true,
      currentWindow: true,
      url: "*://*.roblox.com/*"
    });

    if (activeRobloxTabs.length > 0 && activeRobloxTabs[0].id !== undefined) {
      await chrome.tabs.update(activeRobloxTabs[0].id, { url: nextUrl });
    } else {
      const windowRobloxTabs = await chrome.tabs.query({
        currentWindow: true,
        url: "*://*.roblox.com/*"
      });
      if (windowRobloxTabs.length > 0 && windowRobloxTabs[0].id !== undefined) {
        await chrome.tabs.update(windowRobloxTabs[0].id, { url: nextUrl, active: true });
      } else if (!options.silentIfBridgeDown) {
        await chrome.tabs.create({ url: nextUrl });
      }
    }

    return {
      ok: true,
      username: importedUsername,
      userId: payload.userId || null
    };
  } finally {
    importInFlight = false;
  }
}
