// RoadPopup JavaScript module for Blazor interop

let roadPopupInstance = null;

export function setRoadPopupInstance(instance) {
    roadPopupInstance = instance;
    // Also set it globally so the map click handler can find it
    window.roadPopupInstance = instance;
}

export function clearRoadPopupInstance() {
    roadPopupInstance = null;
    window.roadPopupInstance = null;
}

export function getRoadPopupInstance() {
    return roadPopupInstance;
}