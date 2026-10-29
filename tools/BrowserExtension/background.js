const BRIDGE_ENDPOINT = "http://127.0.0.1:48123/import";
const ROBLOX_COOKIE_URL = "https://www.roblox.com/";
const ROBLOX_LOGIN_URL = "https://www.roblox.com/login";
const COOKIE_NAME = ".ROBLOSECURITY";

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

async function handleImportRequest() {
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
      error: "Cannot connect to RbxManager (127.0.0.1:48123). Open the Add Account window in RbxManager first."
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

  // Remove ONLY .ROBLOSECURITY so anti-bot/session cookies remain intact
  // and the server-side token is not invalidated by a logout request.
  const removeDetails = {
    url: ROBLOX_COOKIE_URL,
    name: COOKIE_NAME
  };
  if (cookie.storeId) {
    removeDetails.storeId = cookie.storeId;
  }
  await chrome.cookies.remove(removeDetails);

  const activeRobloxTabs = await chrome.tabs.query({
    active: true,
    currentWindow: true,
    url: "*://*.roblox.com/*"
  });

  if (activeRobloxTabs.length > 0 && activeRobloxTabs[0].id !== undefined) {
    await chrome.tabs.update(activeRobloxTabs[0].id, { url: ROBLOX_LOGIN_URL });
  } else {
    const windowRobloxTabs = await chrome.tabs.query({
      currentWindow: true,
      url: "*://*.roblox.com/*"
    });
    if (windowRobloxTabs.length > 0 && windowRobloxTabs[0].id !== undefined) {
      await chrome.tabs.update(windowRobloxTabs[0].id, { url: ROBLOX_LOGIN_URL, active: true });
    } else {
      await chrome.tabs.create({ url: ROBLOX_LOGIN_URL });
    }
  }

  return {
    ok: true,
    username: payload.username || "Roblox Account",
    userId: payload.userId || null
  };
}
