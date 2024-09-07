console.log(window.Telegram);



let styles = [];

// Stub function to simulate loading styles from backend via WebSocket
function loadStyles() {
    return [
        {
            id: 1,
            name: 'BananaXL Quality Tags',
            prompt: 'best quality, masterpiece, 4K, 8K, detailed lighting',
            negativePrompt: 'bad quality, worst quality',
            enabled: true,
            sortOrder: null
        },
        {
            id: 2,
            name: 'Night Forest',
            prompt: 'outdoors, night time, moon, forest',
            negativePrompt: '',
            enabled: false,
            sortOrder: 3
        },
        {
            id: 3,
            name: 'Avoid Humans',
            prompt: '',
            negativePrompt: 'human, human penis, glans',
            enabled: true,
            sortOrder: 2
        },
        {
            id: 4,
            name: 'Avoid Children',
            prompt: '',
            negativePrompt: 'child, cub, shota, loli, underage',
            enabled: false,
            sortOrder: null
        },
        {
            id: 5,
            name: 'Sunset Beach',
            prompt: 'beach, sunset, ocean waves, calm',
            negativePrompt: '',
            enabled: true,
            sortOrder: 1
        },
        {
            id: 6,
            name: 'Urban Cityscape',
            prompt: 'city skyline, buildings, cars, people',
            negativePrompt: 'crowds, heavy traffic',
            enabled: true,
            sortOrder: null
        }
    ];
}

// Stub functions for enable, update, resort, delete
function enableStyle(styleId, isEnabled) {
    const style = styles.find(s => s.id === styleId);
    if (style) {
        style.enabled = isEnabled;
        console.log(`Quick interface: Style ${styleId} is now ${isEnabled ? 'enabled' : 'disabled'}`);
    }
}

function updateStyle(styleId, updatedStyle) {
    const index = styles.findIndex(s => s.id === styleId);
    if (index !== -1) {
        styles[index] = updatedStyle;
        console.log(`Style ${styleId} has been updated.`, updatedStyle);
    } else {
        styles.push(updatedStyle);
        console.log(`New Style ${styleId} has been added.`, updatedStyle);
    }
}

function resortStyles(newOrder) {
    console.log('Resorted styles:', newOrder);
}

function deleteStyle(styleId) {
    styles = styles.filter(style => style.id !== styleId);
    console.log(`Style ${styleId} has been deleted.`);
}

// Load and normalize styles from backend
function loadStylesFromBackend() {
    styles = loadStyles();

    styles.sort((a, b) => (a.sortOrder ?? Number.MAX_VALUE) - (b.sortOrder ?? Number.MAX_VALUE));
    styles.forEach((style, index) => {
        style.sortOrder = index + 1;
    });

    renderStyles();
}

function renderStyles() {
    const stylesList = document.getElementById('styles-list');
    stylesList.innerHTML = ''; // Clear existing list

    styles.sort((a, b) => a.sortOrder - b.sortOrder).forEach((style) => {
        renderStyleItem(style);
    });

    window.addEventListener('resize', () => {
        const nameElements = document.querySelectorAll('.name');
        nameElements.forEach(trimName);
    });
}

function renderStyleItem(style) {
    const stylesList = document.getElementById('styles-list');
    const template = document.getElementById('style-template').content.cloneNode(true);
    const styleItem = template.querySelector('.style-item');

    styleItem.setAttribute('data-id', style.id);
    const nameElement = styleItem.querySelector('.name');
    nameElement.textContent = style.name;
    nameElement.setAttribute('data-fullname', style.name);

    const nameInput = styleItem.querySelector('.style-name');
    const promptInput = styleItem.querySelector('.style-prompt');
    const negativePromptInput = styleItem.querySelector('.style-negative-prompt');
    const checkbox = styleItem.querySelector('.enabled-toggle');
    const saveButton = styleItem.querySelector('.save-btn');

    nameInput.value = style.name;
    promptInput.value = style.prompt;
    negativePromptInput.value = style.negativePrompt;
    checkbox.checked = style.enabled;

    saveButton.style.display = 'none'; // Hide the save button by default

    // Add event listeners to detect changes
    function showSaveButton() {
        saveButton.style.display = 'block';
    }

    [nameInput, promptInput, negativePromptInput, checkbox].forEach(element => {
        element.addEventListener('input', showSaveButton);
    });

    const grabHandle = styleItem.querySelector('.grab-handle');

    // Drag and Drop: mouse + touch support
    grabHandle.addEventListener('mousedown', (e) => {
        startDrag(e, styleItem);
    });
    grabHandle.addEventListener('touchstart', (e) => {
        startDrag(e, styleItem);
    });

    function startDrag(e, dragItem) {
        e.preventDefault();
        const isTouch = e.type === 'touchstart';
        const moveHandler = isTouch ? touchMove : mouseMove;
        const endHandler = isTouch ? touchEnd : mouseEnd;

        document.addEventListener(isTouch ? 'touchmove' : 'mousemove', moveHandler);
        document.addEventListener(isTouch ? 'touchend' : 'mouseup', endHandler);

        dragItem.classList.add('dragging');
    }

    function mouseMove(event) {
        handleDragOver(event.clientY);
    }

    function touchMove(event) {
        handleDragOver(event.touches[0].clientY);
    }

    function mouseEnd() {
        endDrag();
    }

    function touchEnd() {
        endDrag();
    }

    function endDrag() {
        const dragItem = document.querySelector('.dragging');
        if (dragItem) {
            dragItem.classList.remove('dragging');
            updateSortOrder();
        }

        document.removeEventListener('mousemove', mouseMove);
        document.removeEventListener('mouseup', mouseEnd);
        document.removeEventListener('touchmove', touchMove);
        document.removeEventListener('touchend', touchEnd);
    }

    function handleDragOver(y) {
        const afterElement = getDragAfterElement(stylesList, y);
        const draggingItem = document.querySelector('.dragging');

        if (afterElement == null) {
            stylesList.appendChild(draggingItem);
        } else {
            stylesList.insertBefore(draggingItem, afterElement);
        }
    }

    styleItem.addEventListener('dragstart', (e) => {
        e.dataTransfer.setData('text/plain', styleItem.dataset.id);
        e.dataTransfer.effectAllowed = 'move';
        setTimeout(() => styleItem.classList.add('dragging'), 0); // Visually "lift" the item
    });

    styleItem.addEventListener('dragend', () => {
        styleItem.classList.remove('dragging');
        styleItem.setAttribute('draggable', false);
        updateSortOrder();
    });

    styleItem.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        const afterElement = getDragAfterElement(stylesList, e.clientY);
        const draggingItem = document.querySelector('.dragging');

        if (afterElement == null) {
            stylesList.appendChild(draggingItem);
        } else {
            stylesList.insertBefore(draggingItem, afterElement);
        }
    });

    styleItem.querySelector('.collapse-btn').addEventListener('click', () => {
        const content = styleItem.querySelector('.style-content');
        content.style.display = content.style.display === 'block' ? 'none' : 'block';
        const icon = styleItem.querySelector('.collapse-btn');
        icon.textContent = content.style.display === 'block' ? '▲' : '▼';
    });

    // Handle the quick interface for enabling/disabling via the emoji
    const toggleEnabled = styleItem.querySelector('.toggle-enabled');
    toggleEnabled.textContent = style.enabled ? '✅' : '❌';

    toggleEnabled.addEventListener('click', () => {
        const isEnabled = toggleEnabled.textContent === '❌';
        toggleEnabled.textContent = isEnabled ? '✅' : '❌';
        enableStyle(style.id, isEnabled);

        // Synchronize the checkbox in the expanded view
        checkbox.checked = isEnabled;
    });

    // Save style with the checkbox only updating when saved
    saveButton.addEventListener('click', () => {
        const styleId = parseInt(styleItem.getAttribute('data-id'));

        const name = nameInput.value;
        const prompt = promptInput.value;
        const negativePrompt = negativePromptInput.value;
        const enabled = checkbox.checked;

        if (!name) {
            showError('Name cannot be empty');
            return;
        }

        if (styles.some(s => s.name === name && s.id !== styleId)) {
            showError('Name must be unique');
            return;
        }

        if (!prompt && !negativePrompt) {
            showError('Either prompt or negative prompt must be filled');
            return;
        }

        const updatedStyle = { id: styleId, name, prompt, negativePrompt, enabled };
        updateStyle(styleId, updatedStyle); // Call the updateStyle stub

        // Synchronize the quick interface tickmark based on the saved state
        toggleEnabled.textContent = enabled ? '✅' : '❌';

        // Hide the save button again after saving
        saveButton.style.display = 'none';
    });

    // Open delete confirmation modal
    styleItem.querySelector('.delete-btn').addEventListener('click', () => {
        openDeleteModal(style.id, style.name);
    });

    stylesList.appendChild(styleItem);
    trimName(nameElement);
}

// Trim the name for overflow
function trimName(element) {
    const fullName = element.getAttribute('data-fullname');
    let trimmedName = fullName;
    element.innerHTML = trimmedName;

    while (element.scrollWidth > element.clientWidth && trimmedName.length > 0) {
        trimmedName = trimmedName.slice(0, -1);
        element.innerHTML = trimmedName + '...';
    }
}

// Helper function to find the position to insert dragged element
function getDragAfterElement(container, y) {
    const draggableElements = [...container.querySelectorAll('.style-item:not(.dragging)')];

    return draggableElements.reduce((closest, child) => {
        const box = child.getBoundingClientRect();
        const offset = y - box.top - box.height / 2;
        if (offset < 0 && offset > closest.offset) {
            return { offset: offset, element: child };
        } else {
            return closest;
        }
    }, { offset: Number.NEGATIVE_INFINITY }).element;
}

// Update sortOrder and send updated order to backend
function updateSortOrder() {
    const items = document.querySelectorAll('.style-item');
    let newOrder = [];

    // Reassign sortOrder for all items
    items.forEach((item, index) => {
        const styleId = parseInt(item.getAttribute('data-id'));
        const style = styles.find(s => s.id === styleId);
        if (style) {
            style.sortOrder = index + 1; // Ensure sequential ordering
            newOrder.push({ id: style.id, sortOrder: style.sortOrder });
        }
    });

    // Send updated order to backend
    resortStyles(newOrder);
}

// Modal handling for Add New Style
const modal = document.getElementById('add-style-modal');
const addNewBtn = document.getElementById('add-new-btn');
const closeBtn = document.querySelector('.close-btn');
const saveNewStyleBtn = document.getElementById('save-new-style-btn');

// Open modal
addNewBtn.addEventListener('click', () => {
    modal.style.display = 'block';
});

// Close modal and clear input fields
closeBtn.addEventListener('click', () => {
    closeModal();
});

// Save new style from modal
saveNewStyleBtn.addEventListener('click', () => {
    const name = document.getElementById('new-style-name').value;
    const prompt = document.getElementById('new-style-prompt').value;
    const negativePrompt = document.getElementById('new-style-negative-prompt').value;

    if (!name) {
        showModalError('Name cannot be empty');
        return;
    }

    if (styles.some(s => s.name === name)) {
        showModalError('Name must be unique');
        return;
    }

    if (!prompt && !negativePrompt) {
        showModalError('Either prompt or negative prompt must be filled');
        return;
    }

    const newStyle = {
        id: Date.now(),
        name,
        prompt,
        negativePrompt,
        enabled: false,
        sortOrder: styles.length + 1
    };

    updateStyle(newStyle.id, newStyle); // Add the new style to the list
    renderStyleItem(newStyle); // Render the new style immediately
    closeModal();
});

// Close modal and clear fields
function closeModal() {
    modal.style.display = 'none';
    document.getElementById('new-style-name').value = '';
    document.getElementById('new-style-prompt').value = '';
    document.getElementById('new-style-negative-prompt').value = '';
}

// Show modal-specific error message
function showModalError(message) {
    const modalErrorMsg = document.getElementById('modal-error-msg');
    modalErrorMsg.textContent = message;
    modalErrorMsg.style.display = 'block';

    setTimeout(() => {
        modalErrorMsg.style.display = 'none';
    }, 3000); // Hide after 3 seconds
}

// Load styles on page load
window.onload = loadStylesFromBackend;

// Close modal when clicking outside
window.onclick = function (event) {
    if (event.target === modal) {
        closeModal();
    }
};

// Delete Confirmation Modal
const deleteModal = document.getElementById('delete-style-modal');
const confirmDeleteBtn = document.getElementById('confirm-delete-btn');
const cancelDeleteBtn = document.getElementById('cancel-delete-btn');
const deleteModalText = document.getElementById('delete-modal-text');
let styleToDeleteId = null;

// Open delete confirmation modal
function openDeleteModal(styleId, styleName) {
    styleToDeleteId = styleId;
    deleteModalText.textContent = `Are you sure you want to delete "${trimText(styleName, 20)}"?`;
    deleteModal.style.display = 'block';
}

// Confirm delete
confirmDeleteBtn.addEventListener('click', () => {
    if (styleToDeleteId !== null) {
        deleteStyle(styleToDeleteId); // Call the deleteStyle stub
        document.querySelector(`[data-id="${styleToDeleteId}"]`).remove(); // Remove from DOM
        closeDeleteModal();
    }
});

// Close delete modal
cancelDeleteBtn.addEventListener('click', closeDeleteModal);

function closeDeleteModal() {
    deleteModal.style.display = 'none';
    styleToDeleteId = null;
}

// Close delete modal when clicking outside
window.onclick = function (event) {
    if (event.target === deleteModal) {
        closeDeleteModal();
    }
};

// Utility function to trim text for the delete modal
function trimText(text, maxLength) {
    if (text.length > maxLength) {
        return text.slice(0, maxLength - 3) + '...';
    }
    return text;
}

document.addEventListener('DOMContentLoaded', async function () {
    document.getElementById('spinner').style.display = 'block';
    document.getElementById('main-content').style.display = 'none';
    await ensureWebSocketOpen();
    document.getElementById('spinner').style.display = 'none';
    document.getElementById('main-content').style.display = 'block';
    console.log("Starting page...");
});
