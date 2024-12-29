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

/**
 * Function to list queue items with optional filters.
 * 
 * @param {Object} [options={}] - The options for listing the queue.
 * @param {number} [options.ID] - Specific image ID to fetch.
 * @param {number} [options.UID] - The user ID to filter the queue. Optional for admins.
 * @param {string} [options.Model] - The model filter for the queue items.
 * @param {number} [options.PageSize=50] - The number of items per page.
 * @param {string} [options.Status] - The status filter for the queue items.
 * @param {string} [options.action] - 'new' or 'old' to determine fetch direction.
 * @param {number} [options.lastImageId] - The reference image ID for pagination.
 * @returns {Promise<Array>} - Resolves with the array of queue items.
 */
function ListQueue({ ID, UID, Model, PageSize, Status, action, lastImageId } = {}) {
    return new Promise((resolve, reject) => {
        const SeqID = getNextSeqID();
        console.log(`ListQueue called with SeqID: ${SeqID} and parameters:`, { ID, UID, Model, PageSize, Status, action, lastImageId });

        // Construct the message with only defined parameters
        const message = {
            Command: "Queue:List",
            SeqID: SeqID
        };

        if (ID !== undefined) {
            message.ID = ID;
            console.log(`Added ID: ${ID} to message.`);
        }

        if (UID !== undefined) {
            message.UID = UID;
            console.log(`Added UID: ${UID} to message.`);
        }

        if (Model !== undefined) {
            message.Model = Model;
            console.log(`Added Model: ${Model} to message.`);
        }

        if (PageSize !== undefined) {
            message.PageSize = PageSize;
            console.log(`Added PageSize: ${PageSize} to message.`);
        }

        if (Status !== undefined) {
            message.Status = Status;
            console.log(`Added Status: ${Status} to message.`);
        }

        if (action !== undefined) {
            message.action = action;
            console.log(`Added action: ${action} to message.`);
        }

        if ((action === 'old' || action === 'new') && lastImageId !== undefined) {
            message.lastImageId = lastImageId;
            console.log(`Added lastImageId: ${lastImageId} to message.`);
        }

        // Store the promise resolve function and expected command
        pendingRequests[SeqID] = {
            command: "Queue:List",
            resolve: (response) => {
                console.log(`ListQueue received response for SeqID: ${SeqID}`, response);

                // Ensure QueueItems is an array
                if (Array.isArray(response.QueueItems)) {
                    resolve(response.QueueItems);
                } else if (response.QueueItems && typeof response.QueueItems === 'object') {
                    // If QueueItems is an object, convert its values to an array
                    const queueItemsArray = Object.values(response.QueueItems);
                    console.warn(`QueueItems was an object. Converted to array:`, queueItemsArray);
                    resolve(queueItemsArray);
                } else {
                    // If QueueItems is neither an array nor an object, reject the promise
                    console.error(`QueueItems is not in a recognized format:`, response.QueueItems);
                    reject(new TypeError('QueueItems is not an array or object.'));
                }
            },
            reject: (error) => {
                console.error(`ListQueue encountered error for SeqID: ${SeqID}:`, error);
                reject(error);
            }
        };

        // Send the message to the WebSocket
        sendMessage(message);
        console.log(`Message sent for SeqID: ${SeqID}`);
    }).catch((error) => {
        console.error('Error in ListQueue Promise:', error);
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
