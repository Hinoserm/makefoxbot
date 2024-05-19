document.addEventListener('DOMContentLoaded', function() {

const textBox = document.querySelector('.text-box');
const sendButton = document.querySelector('.send-button');

function addChat(content) {
	let chatList = document.querySelector('.chat-list');
	let newChat = document.createElement('div');
	newChat.className = 'chat';
	newChat.textContent = content;
	newChat.title = content;

	let closeButton = document.createElement('div');
	closeButton.className = 'close-button';
	closeButton.innerHTML = '&times;';
	newChat.appendChild(closeButton);

	newChat.addEventListener('click', function(event) {
		console.log('Chat:', newChat.title);
	});

	closeButton.addEventListener('click', function(event) {
		console.log('Close:', newChat.title);
		event.stopPropagation();
		chatList.removeChild(newChat);
	});

	chatList.appendChild(newChat);
}

let chatCounter = 0;
document.querySelector('.new-chat').addEventListener('click', function() {
	chatCounter++;
	let title = 'Chat ' + chatCounter;
	console.log('Open:', title);
	addChat(title);
});

function sendInput() {
	const message = textBox.value;
	if (message.trim() === "")
		return;
	console.log("Send:", message);
	textBox.value = "";
}

textBox.addEventListener('keypress', function(event) {
	if (event.key === 'Enter') {
		if (event.ctrlKey) {
			// Insert a new line if Ctrl+Enter is pressed
			const start = textBox.selectionStart;
			const end = textBox.selectionEnd;
			textBox.value = textBox.value.substring(0, start) + "\n" + textBox.value.substring(end);
			textBox.selectionStart = textBox.selectionEnd = start + 1;
		} else {
			// Call sendInput if only Enter is pressed
			event.preventDefault();
			sendInput();
		}
	}
});

sendButton.addEventListener('click', sendInput);

});

/*


// Construct WebSocket URL based on the current location
const currentUrl = new URL(window.location.href);
const wsProtocol = currentUrl.protocol === 'https:' ? 'wss:' : 'ws:';
const wsHost = currentUrl.host;
const wsPath = '/ws';
const wsUrl = `${wsProtocol}//${wsHost}${wsPath}`;

console.log('URL: ' + wsUrl);

let socket = new WebSocket(wsUrl);

socket.onopen = function () {
    console.log('WebSocket connection established');
    // Optionally request initial data
    socket.send(JSON.stringify({ Command: 'GetChatList' }));
};

socket.onmessage = function (event) {
    const message = JSON.parse(event.data);
    console.log('Received:', message);
    handleMessage(message);
};

let activeChats = {};

function handleMessage(message) {
    switch (message.Command) {
        case 'ListChatTabs':
            message.Chats.forEach(chat => createTab(chat));
            break;
        case 'GetChatMessages':
            displayChatMessages(message.Chats);
            break;
        case 'AutocompleteResponse':
            showAutocompleteSuggestions(message.Suggestions);
            break;
        case 'ChatMsgRecv':
            appendChatMessage(message.ChatMsgRecv);
            break;
        case 'Error':
            alert(message.Message);
            break;
        default:
            console.warn('Unknown command:', message.Command);
    }
}

function createTab(chat) {
    const tabList = document.getElementById('tab-list');
    const tab = document.createElement('li');
    tab.textContent = `Chat with ${chat.toUser}`;
    tab.onclick = () => selectChat(chat.toUser);
    tabList.insertBefore(tab, document.getElementById('add-tab'));
    activeChats[chat.toUser] = { tab, messages: [] };
}

function selectChat(username) {
    // Remove active class from all tabs
    document.querySelectorAll('#tab-list li').forEach(li => li.classList.remove('active'));
    // Add active class to selected tab
    activeChats[username].tab.classList.add('active');
    // Show chat content
    displayChatMessages(activeChats[username].messages);
}

function displayChatMessages(messages) {
    const chatContent = document.getElementById('chat-content');
    chatContent.innerHTML = '';
    messages.forEach(message => {
        const messageDiv = document.createElement('div');
        messageDiv.textContent = `${message.FromUID}: ${message.MessageText}`;
        chatContent.appendChild(messageDiv);
    });
}

function appendChatMessage(message) {
    const username = message.FromUID === activeUser.UID ? message.ToUID : message.FromUID;
    if (activeChats[username]) {
        activeChats[username].messages.push(message);
        if (activeChats[username].tab.classList.contains('active')) {
            displayChatMessages(activeChats[username].messages);
        }
    }
}

function showAddChatModal() {
    document.getElementById('addChatModal').style.display = 'flex';
}

function hideAddChatModal() {
    document.getElementById('addChatModal').style.display = 'none';
}

function addChat() {
    const username = document.getElementById('usernameInput').value;
    socket.send(JSON.stringify({ Command: 'NewChat', Username: username }));
    hideAddChatModal();
}

function fetchAutocomplete() {
    const query = document.getElementById('usernameInput').value;
    socket.send(JSON.stringify({ Command: 'Autocomplete', Query: query }));
}

function showAutocompleteSuggestions(suggestions) {
    const autocompleteList = document.getElementById('autocomplete-list');
    autocompleteList.innerHTML = '';
    suggestions.forEach(suggestion => {
        const li = document.createElement('li');
        li.textContent = suggestion.Display;
        li.onclick = () => {
            document.getElementById('usernameInput').value = suggestion.Paste;
            autocompleteList.innerHTML = '';
        };
        autocompleteList.appendChild(li);
    });
}

// Close modal when clicking outside of it
window.onclick = function (event) {
    if (event.target === document.getElementById('addChatModal')) {
        hideAddChatModal();
    }
}
*/