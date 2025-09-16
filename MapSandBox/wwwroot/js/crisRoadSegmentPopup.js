// CrisRoadSegmentPopup JavaScript module for Blazor interop

let crisRoadSegmentPopupInstance = null;

export function setCrisRoadSegmentPopupInstance(instance) {
    crisRoadSegmentPopupInstance = instance;
    // Also set it globally so the map click handler can find it
    window.crisRoadSegmentPopupInstance = instance;
}

export function clearCrisRoadSegmentPopupInstance() {
    crisRoadSegmentPopupInstance = null;
    window.crisRoadSegmentPopupInstance = null;
}

export function getCrisRoadSegmentPopupInstance() {
    return crisRoadSegmentPopupInstance;
}