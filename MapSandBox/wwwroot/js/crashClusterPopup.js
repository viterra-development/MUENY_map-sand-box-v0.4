// JavaScript module for crash cluster popup interactions
let crashClusterPopupInstance = null;

export function setCrashClusterPopupInstance(instance) {
    crashClusterPopupInstance = instance;
    // Also set it globally so the map click handler can find it
    window.crashClusterPopupInstance = instance;
}

export function clearCrashClusterPopupInstance() {
    crashClusterPopupInstance = null;
    window.crashClusterPopupInstance = null;
}

export function showCrashCluster(clusterData) {
    if (crashClusterPopupInstance) {
        crashClusterPopupInstance.invokeMethodAsync('ShowClusterPopupFromJS', clusterData);
    }
}

