let crashPopupInstance = null;

export function setCrashPopupInstance(dotNetObjectReference) {
    crashPopupInstance = dotNetObjectReference;
}

export function clearCrashPopupInstance() {
    crashPopupInstance = null;
}

export function showCrashPopup(crashData) {
    if (crashPopupInstance) {
        crashPopupInstance.invokeMethodAsync('ShowPopupFromJS', crashData);
    }
}