// CrisIntersectionPopup JavaScript module for Blazor interop

let crisIntersectionPopupInstance = null;

export function setCrisIntersectionPopupInstance(instance) {
    crisIntersectionPopupInstance = instance;
    window.crisIntersectionPopupInstance = instance;
}

export function clearCrisIntersectionPopupInstance() {
    crisIntersectionPopupInstance = null;
    window.crisIntersectionPopupInstance = null;
}

export function getCrisIntersectionPopupInstance() {
    return crisIntersectionPopupInstance;
}
