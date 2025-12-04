// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Toast notification system
function showToast(message, type = 'info', duration = 5000) {
    const toastContainer = document.getElementById('toast-container');
    if (!toastContainer) return;

    const toastId = 'toast-' + Date.now();
    const bgClass = type === 'success' ? 'bg-success' : 
                    type === 'error' || type === 'danger' ? 'bg-danger' : 
                    type === 'warning' ? 'bg-warning text-dark' : 'bg-info';
    
    const icon = type === 'success' ? 'bi-check-circle-fill' :
                 type === 'error' || type === 'danger' ? 'bi-x-circle-fill' :
                 type === 'warning' ? 'bi-exclamation-triangle-fill' : 'bi-info-circle-fill';

    const toastHtml = `
        <div id="${toastId}" class="toast" role="alert" aria-live="assertive" aria-atomic="true" data-bs-delay="${duration}">
            <div class="toast-header ${bgClass} text-white">
                <i class="bi ${icon} me-2"></i>
                <strong class="me-auto">${type.charAt(0).toUpperCase() + type.slice(1)}</strong>
                <button type="button" class="btn-close btn-close-white" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
            <div class="toast-body">
                ${message}
            </div>
        </div>
    `;

    toastContainer.insertAdjacentHTML('beforeend', toastHtml);
    const toastElement = document.getElementById(toastId);
    const toast = new bootstrap.Toast(toastElement);
    toast.show();

    // Remove element after it's hidden
    toastElement.addEventListener('hidden.bs.toast', function () {
        toastElement.remove();
    });
}

// Helper functions for common toast types
function showSuccessToast(message, duration) {
    showToast(message, 'success', duration);
}

function showErrorToast(message, duration) {
    showToast(message, 'error', duration);
}

function showWarningToast(message, duration) {
    showToast(message, 'warning', duration);
}

function showInfoToast(message, duration) {
    showToast(message, 'info', duration);
}
