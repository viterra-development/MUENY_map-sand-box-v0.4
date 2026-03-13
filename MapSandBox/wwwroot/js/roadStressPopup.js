// RoadStressPopup JavaScript module for Blazor interop

let roadStressPopupInstance = null;

export function setRoadStressPopupInstance(instance) {
    roadStressPopupInstance = instance;
    window.roadStressPopupInstance = instance;
}

export function clearRoadStressPopupInstance() {
    roadStressPopupInstance = null;
    window.roadStressPopupInstance = null;
}

export function getRoadStressPopupInstance() {
    return roadStressPopupInstance;
}
