// SoilPopup JavaScript module for Blazor interop

let soilPopupInstance = null;

export function setSoilPopupInstance(instance) {
    soilPopupInstance = instance;
    // Also set it globally so the map click handler can find it
    window.soilPopupInstance = instance;
}

export function clearSoilPopupInstance() {
    soilPopupInstance = null;
    window.soilPopupInstance = null;
}

export function getSoilPopupInstance() {
    return soilPopupInstance;
}

