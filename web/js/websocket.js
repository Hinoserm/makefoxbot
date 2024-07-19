// WebSocket URL construction
const currentUrl = new URL(window.location.href);
const wsProtocol = currentUrl.protocol === 'https:' ? 'wss:' : 'ws:';
const wsHost = currentUrl.host;
const wsPath = '/ws';
const wsUrl = `${wsProtocol}//${wsHost}${wsPath}`;

console.log("Websocket URL:" + wsUrl);

// Create a new WebSocket connection
let websocket = new WebSocket(wsUrl);

// Keep track of the sequence number
let SeqID = 1;

// Store the pending promises and their commands
const pendingRequests = {};

// Store the WebSocket connection status
let isWebSocketOpen = false;

// Function to generate a unique sequence number
function getNextSeqID() {
    return ++SeqID;
}

function websocketOnOpen() {
    console.log('Connected to WebSocket');
    sendPing();
}

function websocketOnMessage(event) {
    const response = JSON.parse(event.data);
    console.log('Received message:', response);

    if (response.Command === "Pong") {
        isWebSocketOpen = true;
        resolveOpenPromise();
    } else {
        const SeqID = response.SeqID;
        if (SeqID && pendingRequests[SeqID]) {
            const { command, resolve, reject } = pendingRequests[SeqID];

            if (response.Command === command && response.Success) {
                resolve(response);
            } else {
                reject(new Error(response.Error || 'Operation failed.'));
            }

            // Clean up the pending request
            delete pendingRequests[SeqID];
        } else {
            console.warn('No pending request for received SeqID:', SeqID);
        }
    }
}

function websocketOnError(err) {
    console.error('WebSocket error:', err);
    document.body.innerHTML = '<div style="color: red; text-align: center; margin-top: 20px;">Connection to server lost. Please try again.</div>';
    websocket.onclose = null;
}

function websocketOnClose() {
    console.log('WebSocket connection closed. Attempting to reconnect...');
    isWebSocketOpen = false;
    setTimeout(() => {
        websocket = new WebSocket(wsUrl);
        websocket.onopen = websocketOnOpen;
        websocket.onmessage = websocketOnMessage;
        websocket.onerror = websocketOnError;
        websocket.onclose = websocketOnClose;
    }, 500);
}

// Promise to track the WebSocket connection status
let openPromiseResolve;
let openPromise = new Promise((resolve) => {
    openPromiseResolve = resolve;
});

function resolveOpenPromise() {
    if (openPromiseResolve) {
        openPromiseResolve();
        openPromiseResolve = null;
    }
}

// Function to ensure the WebSocket is open before sending a message
function ensureWebSocketOpen() {
    if (isWebSocketOpen) {
        return Promise.resolve();
    } else {
        return openPromise;
    }
}

// Function to send a message to the WebSocket
function sendMessage(message) {
    ensureWebSocketOpen().then(() => {
        console.log('Sending message:', message);
        websocket.send(JSON.stringify(message));
    }).catch((error) => {
        console.error('Failed to send message:', error);
    });
}

// Function to send a ping message to the WebSocket
function sendPing() {
    if (!isWebSocketOpen) {
        websocket.send(JSON.stringify({ "Command": "Ping" }));
        setTimeout(sendPing, 50); // Send another ping in 50ms if not open
    }
}

// Function to calculate reward days
function CalcRewardDays(amount) {
    console.log('Calculating reward days...');

    return new Promise((resolve, reject) => {
        const SeqID = getNextSeqID();
        const message = {
            Command: "Payments:CalcRewardDays",
            Amount: amount,
            SeqID: SeqID
        };

        // Store the promise resolve function and expected command
        pendingRequests[SeqID] = {
            command: "Payments:CalcRewardDays",
            resolve: (response) => {
                resolve(response.Days); // Resolve with the number of days
            },
            reject
        };

        // Send the message to the WebSocket
        sendMessage(message);
    });
}

// Function to process payment
function ProcessPayment(payment_uuid, provider, amount, charge_id) {
    return new Promise((resolve, reject) => {
        const SeqID = getNextSeqID();
        const message = {
            Command: "Payments:Process",
            PaymentUUID: payment_uuid,
            Provider: provider,
            Amount: amount,
            ChargeID: charge_id,
            SeqID: SeqID
        };

        // Store the promise resolve function and expected command
        pendingRequests[SeqID] = { command: "Payments:Process", resolve, reject };

        // Send the message to the WebSocket
        sendMessage(message);
    }).catch((error) => {
        console.error('Error in ProcessPayment:', error);
        throw error; // Re-throw the error to be caught in the calling function
    });
}

// Assign WebSocket event handlers
websocket.onopen = websocketOnOpen;
websocket.onmessage = websocketOnMessage;
websocket.onerror = websocketOnError;
websocket.onclose = websocketOnClose;

// Make the CalcRewardDays and ProcessPayment functions available globally
window.CalcRewardDays = CalcRewardDays;
window.ProcessPayment = ProcessPayment;

// Ensure other scripts wait for the WebSocket connection
window.ensureWebSocketOpen = ensureWebSocketOpen;
