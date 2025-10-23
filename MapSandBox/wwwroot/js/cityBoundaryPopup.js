let cityBoundaryPopupInstance = null;

export function setCityBoundaryPopupInstance(dotNetObjectReference) {
    cityBoundaryPopupInstance = dotNetObjectReference;
}

export function clearCityBoundaryPopupInstance() {
    cityBoundaryPopupInstance = null;
}

export function showCityBoundaryPopup(cityData) {
    if (cityBoundaryPopupInstance) {
        cityBoundaryPopupInstance.invokeMethodAsync('ShowPopupFromJS', cityData);
    }
}
