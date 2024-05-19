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

function foxInitializeWebSocket() {
    console.log('Initializing WebSocket');
    socket = new WebSocket(wsUrl);

    socket.onopen = function () {
        console.log('WebSocket connection established');
        isSocketOpen = true;
        // Wait for a moment before sending the initial command
        setTimeout(foxSendGetChatListCommand, 1000);
    };

    socket.onmessage = function (event) {
        console.log('WebSocket message received');
        const message = JSON.parse(event.data);
        console.log('Received:', message);
        foxHandleMessage(message);
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

function foxSendGetChatListCommand() {
    if (isSocketOpen && socket.readyState === WebSocket.OPEN) {
        const command = { Command: 'Chat:List' };
        console.log('Sending Chat:List command:', JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('Chat:List command sent');
    } else {
        console.error('WebSocket is not open. Cannot send Chat:List command');
    }
}

let activeChats = {};
let activeUser = null; // Assuming we have a way to set the active user

function foxHandleMessage(message) {
    console.log('Handling message:', message);
    switch (message.Command) {
        case 'Chat:List':
            console.log('Processing Chat:List:', message.Chats);
            foxClearTabs();
            message.Chats.forEach(chat => foxCreateTab(chat));
            break;
        case 'Chat:GetMessages':
            console.log('Processing Chat:GetMessages:', message.Messages);
            foxDisplayChatMessages(message.ChatID, message.Messages);
            break;
        case 'Chat:NewMessage':
            console.log('Processing Chat:NewMessage:', message.Message);
            foxAppendChatMessage(message.Message);
            break;
        case 'Chat:New':
            if (message.Error) {
                console.error('Error creating new chat:', message.Error);
                alert(message.Error);
            } else {
                console.log('New chat created with ChatID:', message.ChatID);
                foxSendGetChatListCommand();
            }
            break;
        case 'AutocompleteResponse':
            console.log('Processing AutocompleteResponse:', message.Suggestions);
            foxShowAutocompleteSuggestions(message.Suggestions);
            break;
        case 'Error':
            console.error('Processing Error:', message.Message);
            alert(message.Message);
            break;
        default:
            console.warn('Unknown command:', message.Command);
    }
}

function foxCreateTab(chat) {
    console.log('Creating tab for:', chat);
    const tabList = document.getElementById('tab-list');
    const tab = document.createElement('li');
    tab.id = `tab-${chat.ChatID}`;

    const tabText = document.createElement('span');
    tabText.textContent = `${chat.DisplayName}`;
    tabText.onclick = () => {
        console.log('Tab clicked:', chat.ChatID);
        foxSelectChat(chat.ChatID);
        foxSendGetChatMessagesCommand(chat.ChatID);
    };

    const closeButton = document.createElement('button');
    closeButton.textContent = 'X';
    closeButton.onclick = (e) => {
        e.stopPropagation();
        console.log('Closing tab:', chat.ChatID);
        foxDeleteChat(chat.ChatID);
    };

    tab.appendChild(tabText);
    tab.appendChild(closeButton);
    tabList.insertBefore(tab, document.getElementById('add-tab'));
    activeChats[chat.ChatID] = { tab, toUser: chat.ToUID, messages: [] };
}

function foxSelectChat(tabId) {
    console.log('Selecting chat:', tabId);
    activeTabId = tabId;
    // Remove active class from all tabs
    document.querySelectorAll('#tab-list li').forEach(li => li.classList.remove('active'));
    // Add active class to selected tab
    activeChats[tabId].tab.classList.add('active');
    // Show chat content
    foxDisplayChatMessages(tabId, activeChats[tabId].messages);
}

function foxSendGetChatMessagesCommand(chatId) {
    if (isSocketOpen && socket.readyState === WebSocket.OPEN) {
        const command = { Command: 'Chat:GetMessages', ChatID: chatId };
        console.log('Sending Chat:GetMessages command for ChatID:', chatId, JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('Chat:GetMessages command sent for ChatID:', chatId);
    } else {
        console.error('WebSocket is not open. Cannot send Chat:GetMessages command');
    }
}

function foxDisplayChatMessages(chatId, messages) {
    console.log('Displaying messages for chat:', chatId);
    const chatContent = document.getElementById('chat-content');
    chatContent.innerHTML = '';
    messages.forEach(message => foxAppendChatMessage(message));
}

function foxAppendChatMessage(message) {
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

function foxDeleteChat(chatId) {
    if (isSocketOpen && socket.readyState === WebSocket.OPEN) {
        const command = { Command: 'Chat:Delete', ChatID: chatId };
        console.log('Sending Chat:Delete command:', JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('Chat:Delete command sent for ChatID:', chatId);
        foxRemoveTab(chatId);
    } else {
        console.error('WebSocket is not open. Cannot send Chat:Delete command');
    }
}

function foxRemoveTab(chatId) {
    console.log('Removing tab:', chatId);
    const tab = document.getElementById(`tab-${chatId}`);
    if (tab) {
        tab.remove();
        delete activeChats[chatId];
    }
}

function foxClearTabs() {
    console.log('Clearing all tabs');
    const tabList = document.getElementById('tab-list');
    while (tabList.firstChild && tabList.firstChild.id !== 'add-tab') {
        tabList.removeChild(tabList.firstChild);
    }
    activeChats = {};
}

function foxShowAddChatModal() {
    console.log('Showing Add Chat Modal');
    document.getElementById('addChatModal').style.display = 'flex';
}

function foxHideAddChatModal() {
    console.log('Hiding Add Chat Modal');
    document.getElementById('addChatModal').style.display = 'none';
}

function foxAddChat() {
    const username = document.getElementById('usernameInput').value;
    console.log('Adding chat for username:', username);
    const command = { Command: 'Chat:New', Username: username };
    console.log('Sending Chat:New command:', JSON.stringify(command));
    socket.send(JSON.stringify(command));
    console.log('Chat:New command sent');
    foxHideAddChatModal();
}

function foxFetchAutocomplete() {
    const query = document.getElementById('usernameInput').value;
    console.log('Fetching autocomplete for query:', query);
    const command = { Command: 'Autocomplete', Query: query };
    console.log('Sending Autocomplete command:', JSON.stringify(command));
    socket.send(JSON.stringify(command));
    console.log('Autocomplete command sent');
}

function foxShowAutocompleteSuggestions(suggestions) {
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
function foxSendMessage() {
    const messageInput = document.getElementById('messageInput');
    const messageText = messageInput.value;
    if (messageText.trim() !== '' && activeTabId !== null) {
        const command = { Command: 'Chat:SendMessage', ChatID: activeTabId, Message: messageText };
        console.log('Sending Chat:SendMessage command:', JSON.stringify(command));
        socket.send(JSON.stringify(command));
        console.log('Chat:SendMessage command sent');
        messageInput.value = ''; // Clear the input
    } else {
        console.error('Cannot send message. Either message is empty or no active chat selected.');
    }
}

// Event listener to send a message when Enter key is pressed
document.getElementById('messageInput').addEventListener('keypress', function (event) {
    if (event.key === 'Enter') {
        event.preventDefault();
        foxSendMessage();
    }
});

// Close modal when clicking outside of it
window.onclick = function (event) {
    if (event.target === document.getElementById('addChatModal')) {
        console.log('Click outside modal, hiding Add Chat Modal');
        foxHideAddChatModal();
    }
}

// Initialize WebSocket and fetch initial data on load
window.onload = function () {
    console.log('Window loaded, initializing WebSocket');
    foxInitializeWebSocket();
}
