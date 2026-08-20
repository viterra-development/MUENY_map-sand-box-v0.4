// Error reporting and debugging utilities for map operations

let errorLog = [];
const maxErrorLogSize = 50;

// Centralized error logging
export function logError(source, error, context = {}) {
    const errorEntry = {
        timestamp: new Date().toISOString(),
        source: source,
        error: {
            name: error.name || 'Unknown',
            message: error.message || 'No message',
            stack: error.stack || 'No stack trace'
        },
        context: context,
        userAgent: navigator.userAgent,
        url: window.location.href
    };

    errorLog.push(errorEntry);

    // Keep log size manageable
    if (errorLog.length > maxErrorLogSize) {
        errorLog = errorLog.slice(-maxErrorLogSize);
    }

    // Log to console with formatting
    console.group(`[${source}] Error Report`);
    console.error('Error:', error);
    console.log('Context:', context);
    console.log('Timestamp:', errorEntry.timestamp);
    console.groupEnd();

    // Store in localStorage for debugging
    try {
        localStorage.setItem('map-error-log', JSON.stringify(errorLog));
    } catch (storageError) {
        console.warn('Could not save error log to localStorage:', storageError);
    }
}

// Get error history for debugging
export function getErrorLog() {
    return [...errorLog];
}

// Clear error log
export function clearErrorLog() {
    errorLog = [];
    try {
        localStorage.removeItem('map-error-log');
    } catch (e) {
        console.warn('Could not clear error log from localStorage:', e);
    }
}

// Initialize error tracking
export function initializeErrorTracking() {
    // Load existing errors from localStorage
    try {
        const stored = localStorage.getItem('map-error-log');
        if (stored) {
            errorLog = JSON.parse(stored);
            console.log(`[Error-Tracking] Loaded ${errorLog.length} previous errors from storage`);
        }
    } catch (e) {
        console.warn('Could not load error log from localStorage:', e);
    }

    // Global error handlers
    window.addEventListener('error', (event) => {
        logError('Global', event.error, {
            filename: event.filename,
            lineno: event.lineno,
            colno: event.colno
        });
    });

    window.addEventListener('unhandledrejection', (event) => {
        logError('UnhandledPromise', event.reason, {
            type: 'Promise rejection'
        });
    });

    console.log('[Error-Tracking] Error tracking initialized');
}

// Performance monitoring for map operations
export function measurePerformance(operationName, operation) {
    const startTime = performance.now();
    const startMark = `${operationName}-start`;
    const endMark = `${operationName}-end`;
    const measureName = `${operationName}-duration`;

    performance.mark(startMark);

    try {
        const result = operation();

        // Handle both sync and async operations
        if (result && typeof result.then === 'function') {
            return result.then(
                (value) => {
                    performance.mark(endMark);
                    performance.measure(measureName, startMark, endMark);
                    const duration = performance.now() - startTime;
                    console.log(`[Performance] ${operationName} completed in ${duration.toFixed(2)}ms`);
                    return value;
                },
                (error) => {
                    performance.mark(endMark);
                    performance.measure(measureName, startMark, endMark);
                    const duration = performance.now() - startTime;
                    logError('Performance', error, { operationName, duration });
                    throw error;
                }
            );
        } else {
            performance.mark(endMark);
            performance.measure(measureName, startMark, endMark);
            const duration = performance.now() - startTime;
            console.log(`[Performance] ${operationName} completed in ${duration.toFixed(2)}ms`);
            return result;
        }
    } catch (error) {
        performance.mark(endMark);
        performance.measure(measureName, startMark, endMark);
        const duration = performance.now() - startTime;
        logError('Performance', error, { operationName, duration });
        throw error;
    }
}

// Health check for map components
export function healthCheck() {
    const health = {
        timestamp: new Date().toISOString(),
        errors: errorLog.length,
        memory: performance.memory ? {
            used: Math.round(performance.memory.usedJSHeapSize / 1024 / 1024),
            total: Math.round(performance.memory.totalJSHeapSize / 1024 / 1024),
            limit: Math.round(performance.memory.jsHeapSizeLimit / 1024 / 1024)
        } : null,
        performance: {
            marks: performance.getEntriesByType('mark').length,
            measures: performance.getEntriesByType('measure').length
        }
    };

    console.log('[Health-Check] System status:', health);
    return health;
}

// Export error log for debugging
export function exportErrorLog() {
    const data = {
        errorLog: getErrorLog(),
        health: healthCheck(),
        timestamp: new Date().toISOString()
    };

    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `map-error-log-${new Date().toISOString().split('T')[0]}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);

    console.log('[Error-Tracking] Error log exported');
}

// Make utilities available globally for debugging
window.mapDebug = {
    getErrorLog,
    clearErrorLog,
    exportErrorLog,
    healthCheck,
    logError
};