document.addEventListener("DOMContentLoaded", () => {
  const importBtn = document.getElementById("importBtn");
  const statusText = document.getElementById("statusText");

  importBtn.addEventListener("click", () => {
    importBtn.disabled = true;
    statusText.className = "status";
    statusText.textContent = "Validating and saving to RbxManager...";

    chrome.runtime.sendMessage({ type: "IMPORT_ROBLOSECURITY" }, (response) => {
      importBtn.disabled = false;

      if (chrome.runtime.lastError) {
        statusText.className = "status error";
        statusText.textContent = chrome.runtime.lastError.message || "Extension communication failed.";
        return;
      }

      if (!response || !response.ok) {
        statusText.className = "status error";
        statusText.textContent = (response && response.error) ? response.error : "Import failed.";
        return;
      }

      statusText.className = "status success";
      statusText.textContent = `Imported ${response.username}! Ready for next account.`;
    });
  });
});
