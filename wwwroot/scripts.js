// Ensure the WebSocket URL is constructed once
const currentUrl = new URL(window.location.href);
const wsProtocol = currentUrl.protocol === 'https:' ? 'wss:' : 'ws:';
const wsHost = currentUrl.host;
const wsPath = '/ws';
const wsUrl = `${wsProtocol}//${wsHost}${wsPath}`;

console.log('WebSocket URL:', wsUrl);

let socket;
let isSocketOpen = false;
let activeTabId = null; // Track the active tab ID

function initializeWebSocket() {
    console.log('Initializing WebSocket');
    socket = new WebSocket(wsUrl);

    socket.onopen = function () {
        console.log('WebSocket connection established');
        isSocketOpen = true;
        // Wait for a moment before sending the initial command
        setTimeout(sendGetChatListCommand, 1000);
    };

    socket.onmessage = function (event) {
        console.log('WebSocket message received');
        const message = JSON.parse(event.data);
        console.log('Received:', message);
        handleMessage(message);
    };

    socket.onerror = function (error) {
        console.error('WebSocket error:', error);
    };

    socket.onclose = function () {
        console.log('WebSocket connection closed');
        isSocketOpen = false;
        // Optionally attempt to reconnect
    };
}

function sendGetChatListCommand() {
    if (isSocketOpen && socket.readyState === WebSocket.OPEN) {
        const command = { Command: 'GetChatList' };
        console.log('Sending GetChatList command:', JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('GetChatList command sent');
    } else {
        console.error('WebSocket is not open. Cannot send GetChatList command');
    }
}

let activeChats = {};
let activeUser = null; // Assuming we have a way to set the active user

function handleMessage(message) {
    console.log('Handling message:', message);
    switch (message.Command) {
        case 'ListChatTabs':
            console.log('Processing ListChatTabs:', message.Chats);
            clearTabs();
            message.Chats.forEach(chat => createTab(chat));
            break;
        case 'GetChatMessages':
            console.log('Processing GetChatMessages:', message.Chats);
            displayChatMessages(message.ChatID, message.Chats);
            break;
        case 'AutocompleteResponse':
            console.log('Processing AutocompleteResponse:', message.Suggestions);
            showAutocompleteSuggestions(message.Suggestions);
            break;
        case 'ChatMsgRecv':
        case 'ChatMessage':
            console.log('Processing ChatMsgRecv:', message.Content);
            appendChatMessage(message.Content);
            break;
        case 'Error':
            console.error('Processing Error:', message.Message);
            alert(message.Message);
            break;
        default:
            console.warn('Unknown command:', message.Command);
    }
}

function createTab(chat) {
    console.log('Creating tab for:', chat);
    const tabList = document.getElementById('tab-list');
    const tab = document.createElement('li');
    tab.id = `tab-${chat.TabID}`;

    const tabText = document.createElement('span');
    tabText.textContent = `Chat with ${chat.toUser}`;
    tabText.onclick = () => {
        console.log('Tab clicked:', chat.TabID);
        selectChat(chat.TabID);
        sendGetChatMessagesCommand(chat.TabID);
    };

    const closeButton = document.createElement('button');
    closeButton.textContent = 'X';
    closeButton.onclick = (e) => {
        e.stopPropagation();
        console.log('Closing tab:', chat.TabID);
        deleteChat(chat.TabID);
    };

    tab.appendChild(tabText);
    tab.appendChild(closeButton);
    tabList.insertBefore(tab, document.getElementById('add-tab'));
    activeChats[chat.TabID] = { tab, toUser: chat.toUser, messages: [] };
}

function selectChat(tabId) {
    console.log('Selecting chat:', tabId);
    activeTabId = tabId;
    // Remove active class from all tabs
    document.querySelectorAll('#tab-list li').forEach(li => li.classList.remove('active'));
    // Add active class to selected tab
    activeChats[tabId].tab.classList.add('active');
    // Show chat content
    displayChatMessages(tabId, activeChats[tabId].messages);
}

function sendGetChatMessagesCommand(chatId) {
    if (isSocketOpen && socket.readyState === WebSocket.OPEN) {
        const command = { Command: 'GetChatMessages', ChatID: chatId };
        console.log('Sending GetChatMessages command for ChatID:', chatId, JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('GetChatMessages command sent for ChatID:', chatId);
    } else {
        console.error('WebSocket is not open. Cannot send GetChatMessages command');
    }
}

function displayChatMessages(chatId, messages) {
    console.log('Displaying messages for chat:', chatId);
    const chatContent = document.getElementById('chat-content');
    chatContent.innerHTML = '';
    messages.forEach(message => appendChatMessage(message));
}

function appendChatMessage(message) {
    const chatId = message.ChatID || activeTabId; // Handle cases where ChatID may not be explicitly provided
    const selectedChat = activeChats[activeTabId];
    if (selectedChat && (selectedChat.toUser === message.ToUID || selectedChat.toUser === message.FromUID)) {
        console.log('Appending message to chat:', chatId, message);
        selectedChat.messages.push(message);
        const chatContent = document.getElementById('chat-content');
        const messageDiv = document.createElement('div');
        messageDiv.classList.add('chat-message');
        messageDiv.classList.add(message.isOutgoing ? 'outgoing' : 'incoming');
        messageDiv.textContent = `${message.Username}: ${message.MessageText}`;
        chatContent.appendChild(messageDiv);
        chatContent.scrollTop = chatContent.scrollHeight; // Scroll to bottom
    }
}

function deleteChat(chatId) {
    if (isSocketOpen && socket.readyState === WebSocket.OPEN) {
        const command = { Command: 'DeleteChat', ChatID: chatId };
        console.log('Sending DeleteChat command:', JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('DeleteChat command sent for ChatID:', chatId);
        removeTab(chatId);
    } else {
        console.error('WebSocket is not open. Cannot send DeleteChat command');
    }
}

function removeTab(chatId) {
    console.log('Removing tab:', chatId);
    const tab = document.getElementById(`tab-${chatId}`);
    if (tab) {
        tab.remove();
        delete activeChats[chatId];
    }
}

function clearTabs() {
    console.log('Clearing all tabs');
    const tabList = document.getElementById('tab-list');
    while (tabList.firstChild && tabList.firstChild.id !== 'add-tab') {
        tabList.removeChild(tabList.firstChild);
    }
    activeChats = {};
}

function showAddChatModal() {
    console.log('Showing Add Chat Modal');
    document.getElementById('addChatModal').style.display = 'flex';
}

function hideAddChatModal() {
    console.log('Hiding Add Chat Modal');
    document.getElementById('addChatModal').style.display = 'none';
}

function addChat() {
    const username = document.getElementById('usernameInput').value;
    console.log('Adding chat for username:', username);
    const command = { Command: 'NewChat', Username: username };
    console.log('Sending NewChat command:', JSON.stringify(command));
    socket.send(JSON.stringify(command));
    console.log('NewChat command sent');
    hideAddChatModal();
}

function fetchAutocomplete() {
    const query = document.getElementById('usernameInput').value;
    console.log('Fetching autocomplete for query:', query);
    const command = { Command: 'Autocomplete', Query: query };
    console.log('Sending Autocomplete command:', JSON.stringify(command));
    socket.send(JSON.stringify(command));
    console.log('Autocomplete command sent');
}

function showAutocompleteSuggestions(suggestions) {
    console.log('Showing autocomplete suggestions:', suggestions);
    const autocompleteList = document.getElementById('autocomplete-list');
    autocompleteList.innerHTML = '';
    suggestions.forEach(suggestion => {
        const li = document.createElement('li');
        li.textContent = suggestion.Display;
        li.onclick = () => {
            console.log('Autocomplete suggestion clicked:', suggestion.Paste);
            document.getElementById('usernameInput').value = suggestion.Paste;
            autocompleteList.innerHTML = '';
        };
        autocompleteList.appendChild(li);
    });
}

// Function to send a message
function sendMessage() {
    const messageInput = document.getElementById('messageInput');
    const messageText = messageInput.value;
    if (messageText.trim() !== '' && activeTabId !== null) {
        const command = { Command: 'SendChatMessage', ChatID: activeTabId, Message: messageText };
        console.log('Sending SendChatMessage command:', JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('SendChatMessage command sent');
        messageInput.value = ''; // Clear the input
    } else {
        console.error('Cannot send message. Either message is empty or no active chat selected.');
    }
}

// Event listener to send a message when Enter key is pressed
document.getElementById('messageInput').addEventListener('keypress', function (event) {
    if (event.key === 'Enter') {
        event.preventDefault();
        sendMessage();
    }
});

// Close modal when clicking outside of it
window.onclick = function (event) {
    if (event.target === document.getElementById('addChatModal')) {
        console.log('Click outside modal, hiding Add Chat Modal');
        hideAddChatModal();
    }
}

// Initialize WebSocket and fetch initial data on load
window.onload = function () {
    console.log('Window loaded, initializing WebSocket');
    initializeWebSocket();
}
