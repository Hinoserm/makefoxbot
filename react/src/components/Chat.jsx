import React, { useState, useEffect, useRef } from 'react';
import { Container, Grid, Box, Button, TextField, Dialog, DialogActions, DialogContent, DialogTitle, List, ListItem, ListItemText, Typography, Paper } from '@mui/material';

const currentUrl = new URL(window.location.href);
const wsProtocol = currentUrl.protocol === 'https:' ? 'wss:' : 'ws:';
const wsHost = currentUrl.hostname; // Use hostname to avoid port duplication
const wsPort = 5555; // Specify the desired port here
const wsPath = '/ws';
const wsUrl = `${wsProtocol}//${wsHost}:${wsPort}${wsPath}`;

console.log("Websocket URL:", wsUrl); // Output the wsUrl for verification

let globalSocket = null;

const ChatApp = () => {
    const [socket, setSocket] = useState(null);
    const [chats, setChats] = useState([]);
    const [activeChatId, setActiveChatId] = useState(null);
    const [messages, setMessages] = useState({});
    const [newMessageHighlight, setNewMessageHighlight] = useState({});
    const [newChatDialogOpen, setNewChatDialogOpen] = useState(false);
    const [newChatUsername, setNewChatUsername] = useState('');
    const [newMessage, setNewMessage] = useState('');
    const [autocompleteSuggestions, setAutocompleteSuggestions] = useState([]);
    const [loading, setLoading] = useState(true);
    const messageEndRef = useRef(null);

    useEffect(() => {
        initializeWebSocket();
    }, []);

    useEffect(() => {
        if (activeChatId && messageEndRef.current) {
            messageEndRef.current.scrollIntoView({ behavior: 'smooth' });
        }
    }, [messages, activeChatId]);

    const initializeWebSocket = () => {
        const ws = new WebSocket(wsUrl);
        ws.onopen = () => {
            console.log('WebSocket connection established');
            setSocket(ws);
            globalSocket = ws;
            setTimeout(() => {
                sendGetChatListCommand(ws);
                setLoading(false);
            }, 200);
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
                if (activeChatId) {
                    sendGetChatMessagesCommand(activeChatId);
                }
                break;
            case 'Chat:GetMessages':
                console.log('Processing Chat:GetMessages:', message.Messages);
                setMessages(prev => ({ ...prev, [message.ChatID]: message.Messages }));
                break;
            case 'Chat:NewMessage':
                console.log('Processing Chat:NewMessage:', message);
                const { ChatID, Message } = message;
                if (ChatID !== activeChatId) {
                    setNewMessageHighlight(prev => ({ ...prev, [ChatID]: true }));
                }
                setMessages(prev => {
                    const updatedMessages = prev[ChatID] ? [...prev[ChatID], Message] : [Message];
                    return { ...prev, [ChatID]: updatedMessages };
                });
                break;
            case 'Chat:New':
                if (message.Error) {
                    console.error('Error creating new chat:', message.Error);
                    alert(message.Error);
                } else {
                    console.log('New chat created with ChatID:', message.ChatID);
                    setActiveChatId(message.ChatID);
                    sendGetChatListCommand(globalSocket);
                    sendGetChatMessagesCommand(message.ChatID); // Fetch messages for the new chat
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

    const sendGetChatMessagesCommand = (chatId) => {
        if (globalSocket && globalSocket.readyState === WebSocket.OPEN) {
            const command = { Command: 'Chat:GetMessages', ChatID: chatId };
            console.log('Sending Chat:GetMessages command for ChatID:', chatId, JSON.stringify(command));
            globalSocket.send(JSON.stringify(command));
            console.log('Chat:GetMessages command sent for ChatID:', chatId);
        } else {
            console.error('WebSocket is not open. Cannot send Chat:GetMessages command');
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

    const formatDate = (dateString) => {
        const date = new Date(dateString);
        return `${date.toLocaleDateString()} ${date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    };

    return (
        <Container maxWidth="xl" style={{ display: 'flex', height: '100vh', padding: 0 }}>
            {loading && (
                <Box
                    style={{
                        position: 'absolute',
                        top: 0,
                        left: 0,
                        width: '100%',
                        height: '100%',
                        backgroundColor: 'rgba(255, 255, 255, 0.8)',
                        zIndex: 10,
                        display: 'flex',
                        justifyContent: 'center',
                        alignItems: 'center'
                    }}
                >
                    <Typography variant="h6">Loading...</Typography>
                </Box>
            )}
            <Grid container style={{ height: '100%' }}>
                <Grid item style={{ width: '300px', borderRight: '1px solid #ddd', overflowY: 'auto' }}>
                    <List>
                        {chats.map(chat => (
                            <ListItem
                                button
                                key={chat.ChatID}
                                style={chat.ChatID === activeChatId ? { backgroundColor: '#c7d2fe' } :
                                    newMessageHighlight[chat.ChatID] ? { backgroundColor: '#e3f2fd' } : {}}
                                onClick={() => {
                                    console.log('Chat tab clicked:', chat.ChatID);
                                    setActiveChatId(chat.ChatID);
                                    sendGetChatMessagesCommand(chat.ChatID);
                                    setNewMessageHighlight(prev => ({ ...prev, [chat.ChatID]: false }));
                                }}
                            >
                                <Box display="flex" justifyContent="space-between" alignItems="center" width="100%">
                                    <ListItemText primary={chat.DisplayName} />
                                    <Button
                                        onClick={(e) => {
                                            e.stopPropagation();
                                            console.log('Deleting chat:', chat.ChatID);
                                            if (globalSocket && globalSocket.readyState === WebSocket.OPEN) {
                                                const command = { Command: 'Chat:Delete', ChatID: chat.ChatID };
                                                console.log('Sending Chat:Delete command:', JSON.stringify(command));
                                                globalSocket.send(JSON.stringify(command));
                                                console.log('Chat:Delete command sent');
                                                setChats(prev => prev.filter(c => c.ChatID !== chat.ChatID));
                                                if (activeChatId === chat.ChatID) {
                                                    setActiveChatId(null); // Clear the active chat if it's being deleted
                                                }
                                            } else {
                                                console.error('WebSocket is not open. Cannot send Chat:Delete command');
                                            }
                                        }}
                                        style={{ marginLeft: 'auto', marginRight: '-16px' }}  // Adjust marginRight to move the button right
                                    >
                                        X
                                    </Button>
                                </Box>
                            </ListItem>
                        ))}
                        <ListItem>
                            <Box display="flex" justifyContent="center" width="100%">
                                <Button variant="contained" color="primary" onClick={() => setNewChatDialogOpen(true)}>
                                    New Chat
                                </Button>
                            </Box>
                        </ListItem>
                    </List>
                </Grid>
                <Grid item xs style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
                    <Box style={{ flex: 1, overflowY: 'auto', padding: '10px' }}>
                        {activeChatId ? messages[activeChatId] && messages[activeChatId].map((message, index) => (
                            <Paper
                                key={index}
                                style={{
                                    maxWidth: '60%',
                                    padding: '10px',
                                    margin: '5px',
                                    borderRadius: '10px',
                                    backgroundColor: message.isOutgoing ? '#dcf8c6' : '#fff',
                                    border: message.isOutgoing ? 'none' : '1px solid #ddd',
                                    alignSelf: message.isOutgoing ? 'flex-end' : 'flex-start',
                                    marginLeft: message.isOutgoing ? 'auto' : '10px',
                                    marginRight: message.isOutgoing ? '10px' : 'auto'
                                }}
                            >
                                <Typography variant="body1">{`${message.Username}: ${message.Text}`}</Typography>
                                <Typography variant="caption" style={{ textAlign: 'right', display: 'block', marginTop: '4px' }}>
                                    {formatDate(message.Date)}
                                </Typography>
                            </Paper>
                        )) : <Typography style={{ textAlign: 'center', marginTop: '20%' }}>Select a chat</Typography>}
                        <div ref={messageEndRef} />
                    </Box>
                    {activeChatId ? (
                        <Box style={{ display: 'flex', padding: '10px', borderTop: '1px solid #ddd' }}>
                            <TextField
                                fullWidth
                                placeholder="Type a message"
                                value={newMessage}
                                onChange={e => setNewMessage(e.target.value)}
                                onKeyPress={e => {
                                    if (e.key === 'Enter') {
                                        e.preventDefault();
                                        handleSendMessage();
                                    }
                                }}
                            />
                            <Button variant="contained" color="primary" onClick={handleSendMessage}>Send</Button>
                        </Box>
                    ) : <div />}
                </Grid>
            </Grid>
            <Dialog open={newChatDialogOpen} onClose={() => setNewChatDialogOpen(false)}>
                <DialogTitle>Add New Chat</DialogTitle>
                <DialogContent>
                    <TextField
                        autoFocus
                        margin="dense"
                        label="Username"
                        fullWidth
                        value={newChatUsername}
                        onChange={e => {
                            setNewChatUsername(e.target.value);
                            handleAutocomplete(e.target.value);
                        }}
                    />
                    <ul id="autocomplete-list">
                        {autocompleteSuggestions.map((suggestion, index) => (
                            <li
                                key={index}
                                onClick={() => {
                                    setNewChatUsername(suggestion.Paste);
                                    setAutocompleteSuggestions([]);
                                }}
                            >
                                {suggestion.Display}
                            </li>
                        ))}
                    </ul>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setNewChatDialogOpen(false)} color="primary">Cancel</Button>
                    <Button onClick={handleNewChat} color="primary">Add</Button>
                </DialogActions>
            </Dialog>
        </Container>
    );
};

export default ChatApp;
