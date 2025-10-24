let cityBoundaryTooltipInstance = null;

export function setCityBoundaryTooltipInstance(dotNetObjectReference) {
    cityBoundaryTooltipInstance = dotNetObjectReference;
}

export function clearCityBoundaryTooltipInstance() {
    cityBoundaryTooltipInstance = null;
}

export function showCityBoundaryTooltip(cityName, x, y) {
    if (cityBoundaryTooltipInstance) {
        cityBoundaryTooltipInstance.invokeMethodAsync('ShowTooltip', cityName, x, y);
    }
}

export function hideCityBoundaryTooltip() {
    if (cityBoundaryTooltipInstance) {
        cityBoundaryTooltipInstance.invokeMethodAsync('HideTooltip');
    }
}
