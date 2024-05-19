const { useState, useEffect } = React;
const { Container, Box, Button, TextField, Dialog, DialogActions, DialogContent, DialogTitle, List, ListItem, ListItemText, Typography, Paper } = MaterialUI;

// WebSocket URL construction
const currentUrl = new URL(window.location.href);
const wsProtocol = currentUrl.protocol === 'https:' ? 'wss:' : 'ws:';
const wsHost = currentUrl.host;
const wsPath = '/ws';
const wsUrl = `${wsProtocol}//${wsHost}${wsPath}`;

let globalSocket = null;

const ChatApp = () => {
  const [socket, setSocket] = useState(null);
  const [chats, setChats] = useState([]);
  const [activeChatId, setActiveChatId] = useState(null);
  const [messages, setMessages] = useState([]);
  const [newChatDialogOpen, setNewChatDialogOpen] = useState(false);
  const [newChatUsername, setNewChatUsername] = useState('');
  const [newMessage, setNewMessage] = useState('');
  const [autocompleteSuggestions, setAutocompleteSuggestions] = useState([]);

  useEffect(() => {
    initializeWebSocket();
  }, []);

  const initializeWebSocket = () => {
    const ws = new WebSocket(wsUrl);
    ws.onopen = () => {
      console.log('WebSocket connection established');
      setSocket(ws);
      globalSocket = ws;
      sendGetChatListCommand(ws);
    };
    ws.onmessage = (event) => {
      const message = JSON.parse(event.data);
      console.log('WebSocket message received:', message);
      handleMessage(message);
    };
    ws.onerror = (error) => console.error('WebSocket error:', error);
    ws.onclose = () => {
      console.log('WebSocket connection closed, attempting to reconnect...');
      setSocket(null);
      globalSocket = null;
      setTimeout(initializeWebSocket, 1000);
    };
  };

  const sendGetChatListCommand = (ws) => {
    if (ws && ws.readyState === WebSocket.OPEN) {
      const command = { Command: 'Chat:List' };
      console.log('Sending Chat:List command:', JSON.stringify(command));
      ws.send(JSON.stringify(command));
      console.log('Chat:List command sent');
    } else {
      console.error('WebSocket is not open. Cannot send Chat:List command');
    }
  };

  const handleMessage = (message) => {
    console.log('Handling message:', message);
    switch (message.Command) {
      case 'Chat:List':
        console.log('Processing Chat:List:', message.Chats);
        setChats(message.Chats);
        break;
      case 'Chat:GetMessages':
        console.log('Processing Chat:GetMessages:', message.Messages);
        setMessages(message.Messages);
        break;
      case 'Chat:NewMessage':
        console.log('Processing Chat:NewMessage:', message.Message);
        setMessages(prevMessages => [...prevMessages, message.Message]);
        break;
      case 'Chat:New':
        if (message.Error) {
          console.error('Error creating new chat:', message.Error);
          alert(message.Error);
        } else {
          console.log('New chat created with ChatID:', message.ChatID);
          setActiveChatId(message.ChatID);
          sendGetChatListCommand(globalSocket);
        }
        break;
      case 'AutocompleteResponse':
        console.log('Processing AutocompleteResponse:', message.Suggestions);
        setAutocompleteSuggestions(message.Suggestions);
        break;
      default:
        console.warn('Unknown command:', message.Command);
    }
  };

  const handleNewChat = () => {
    console.log('Creating new chat with username:', newChatUsername);
    if (globalSocket && globalSocket.readyState === WebSocket.OPEN) {
      const command = { Command: 'Chat:New', Username: newChatUsername };
      console.log('Sending Chat:New command:', JSON.stringify(command));
      globalSocket.send(JSON.stringify(command));
      console.log('Chat:New command sent');
      setNewChatDialogOpen(false);
      setNewChatUsername('');
    } else {
      console.error('WebSocket is not open. Cannot send Chat:New command');
    }
  };

  const handleSendMessage = () => {
    console.log('Sending message:', newMessage);
    if (globalSocket && globalSocket.readyState === WebSocket.OPEN) {
      if (newMessage.trim() !== '' && activeChatId) {
        const command = { Command: 'Chat:SendMessage', ChatID: activeChatId, Message: newMessage };
        console.log('Sending Chat:SendMessage command:', JSON.stringify(command));
        globalSocket.send(JSON.stringify(command));
        console.log('Chat:SendMessage command sent');
        setNewMessage('');
      } else {
        console.error('Cannot send message. Either message is empty or no active chat selected.');
      }
    } else {
      console.error('WebSocket is not open. Cannot send Chat:SendMessage command');
    }
  };

  const handleAutocomplete = (query) => {
    console.log('Fetching autocomplete suggestions for query:', query);
    if (globalSocket && globalSocket.readyState === WebSocket.OPEN) {
      if (query.trim() !== '') {
        const command = { Command: 'Autocomplete', Query: query };
        console.log('Sending Autocomplete command:', JSON.stringify(command));
        globalSocket.send(JSON.stringify(command));
        console.log('Autocomplete command sent');
      }
    } else {
      console.error('WebSocket is not open. Cannot send Autocomplete command');
    }
  };

  return React.createElement(
    Container,
    { maxWidth: 'xl', style: { display: 'flex', height: '100vh' } },
    React.createElement(
      Box,
      { style: { width: '25%', borderRight: '1px solid #ddd', overflowY: 'auto' } },
      React.createElement(
        List,
        null,
        chats.map(chat =>
          React.createElement(
            ListItem,
            {
              button: true,
              key: chat.ChatID,
              onClick: () => {
                console.log('Chat tab clicked:', chat.ChatID);
                setActiveChatId(chat.ChatID);
                if (globalSocket && globalSocket.readyState === WebSocket.OPEN) {
                  const command = { Command: 'Chat:GetMessages', ChatID: chat.ChatID };
                  console.log('Sending Chat:GetMessages command:', JSON.stringify(command));
                  globalSocket.send(JSON.stringify(command));
                  console.log('Chat:GetMessages command sent');
                } else {
                  console.error('WebSocket is not open. Cannot send Chat:GetMessages command');
                }
              }
            },
            React.createElement(ListItemText, { primary: chat.DisplayName }),
            React.createElement(
              Button,
              {
                onClick: (e) => {
                  e.stopPropagation();
                  console.log('Deleting chat:', chat.ChatID);
                  if (globalSocket && globalSocket.readyState === WebSocket.OPEN) {
                    const command = { Command: 'Chat:Delete', ChatID: chat.ChatID };
                    console.log('Sending Chat:Delete command:', JSON.stringify(command));
                    globalSocket.send(JSON.stringify(command));
                    console.log('Chat:Delete command sent');
                    setChats(prev => prev.filter(c => c.ChatID !== chat.ChatID));
                  } else {
                    console.error('WebSocket is not open. Cannot send Chat:Delete command');
                  }
                }
              },
              'X'
            )
          )
        ),
        React.createElement(
          Button,
          { variant: 'contained', color: 'primary', onClick: () => setNewChatDialogOpen(true) },
          'New Chat'
        )
      )
    ),
    React.createElement(
      Box,
      { style: { flex: 1, display: 'flex', flexDirection: 'column', padding: '10px' } },
      React.createElement(
        Box,
        { style: { flex: 1, overflowY: 'auto', paddingBottom: '10px' } },
        messages.map((message, index) =>
          React.createElement(
            Paper,
            {
              key: index,
              style: {
                maxWidth: '60%',
                padding: '10px',
                margin: '5px',
                borderRadius: '10px',
                backgroundColor: message.isOutgoing ? '#dcf8c6' : '#fff',
                border: message.isOutgoing ? 'none' : '1px solid #ddd',
                alignSelf: message.isOutgoing ? 'flex-end' : 'flex-start',
                marginLeft: message.isOutgoing ? 'auto' : '10px',
                marginRight: message.isOutgoing ? '10px' : 'auto'
              }
            },
            React.createElement(Typography, { variant: 'body1' }, `${message.Username}: ${message.MessageText}`)
          )
        )
      ),
      React.createElement(
        Box,
        { style: { display: 'flex', padding: '10px', borderTop: '1px solid #ddd' } },
        React.createElement(TextField, {
          fullWidth: true,
          placeholder: 'Type a message',
          value: newMessage,
          onChange: e => setNewMessage(e.target.value),
          onKeyPress: e => {
            if (e.key === 'Enter') {
              e.preventDefault();
              handleSendMessage();
            }
          }
        }),
        React.createElement(Button, { variant: 'contained', color: 'primary', onClick: handleSendMessage }, 'Send')
      )
    ),
    React.createElement(
      Dialog,
      { open: newChatDialogOpen, onClose: () => setNewChatDialogOpen(false) },
      React.createElement(DialogTitle, null, 'Add New Chat'),
      React.createElement(
        DialogContent,
        null,
        React.createElement(TextField, {
          autoFocus: true,
          margin: 'dense',
          label: 'Username',
          fullWidth: true,
          value: newChatUsername,
          onChange: e => {
            setNewChatUsername(e.target.value);
            handleAutocomplete(e.target.value);
          }
        }),
        React.createElement(
          'ul',
          { id: 'autocomplete-list' },
          autocompleteSuggestions.map((suggestion, index) =>
            React.createElement(
              'li',
              {
                key: index,
                onClick: () => {
                  setNewChatUsername(suggestion.Paste);
                  setAutocompleteSuggestions([]);
                }
              },
              suggestion.Display
            )
          )
        )
      ),
      React.createElement(
        DialogActions,
        null,
        React.createElement(Button, { onClick: () => setNewChatDialogOpen(false), color: 'primary' }, 'Cancel'),
        React.createElement(Button, { onClick: handleNewChat, color: 'primary' }, 'Add')
      )
    )
  );
};

ReactDOM.render(React.createElement(ChatApp), document.getElementById('root'));
