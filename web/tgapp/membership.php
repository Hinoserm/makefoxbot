<?php
header("Expires: Tue, 01 Jan 2000 00:00:00 GMT");
header("Last-Modified: " . gmdate("D, d M Y H:i:s") . " GMT");
header("Cache-Control: no-store, no-cache, must-revalidate, max-age=0");
header("Cache-Control: post-check=0, pre-check=0", false);
header("Pragma: no-cache");
?>

<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Membership Purchase</title>
    <script src="https://telegram.org/js/telegram-web-app.js"></script>
    <script src="https://js.stripe.com/v3/"></script>
    <script src="https://www.paypal.com/sdk/js?client-id=AcZwu1bLQvpI-h_GfAtpk3VMmqxV15x6URNrASHv72nVv2H5OlsLD8MveJQV3PxPXDCZm7Z7-dvTjm7D&disable-funding=credit,card&intent=capture&commit=true"></script>
    <style>
        body {
            font-family: Arial, sans-serif;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }
        h1, h2 {
            color: #333;
        }
        p {
            font-size: 16px;
        }
        .hidden {
            display: none;
        }
        .payment-method {
            margin-top: 20px;
        }
        button {
            padding: 10px 20px;
            margin-top: 20px;
            background-color: #007bff;
            color: #fff;
            border: none;
            border-radius: 4px;
            cursor: pointer;
        }
        button:hover {
            background-color: #0056b3;
        }
        #card-element {
            background-color: #fff;
            padding: 10px;
            border: 1px solid #ccc;
            border-radius: 4px;
        }
        #stripe-form {
            margin-top: 20px;
        }
        .form-group {
            margin-bottom: 15px;
        }
        .form-group label {
            margin-left: 5px;
        }
    </style>
</head>
<body>

<div id="payment-errors" class="hidden" style="color: red; margin-top: 20px;"></div>

<div id="price-details">
    <h1>Purchase Membership</h1>
    <p id="membership-details"></p>
    <button id="next-button">Next</button>
</div>

<div id="payment-methods" class="hidden">
    <h2>Select Payment Method</h2>
    <div class="form-group">
        <input type="radio" id="stripe" name="payment-method" value="stripe" checked>
        <label for="stripe">Stripe (Credit Card)</label>
    </div>
    <div class="form-group">
        <input type="radio" id="paypal" name="payment-method" value="paypal">
        <label for="paypal">PayPal</label>
    </div>
    <button id="pay-button">Next</button>
</div>

<div id="recurring-option" class="hidden">
    <h2>Recurring Membership</h2>
    <p id="recurring-message"></p>
    <div class="form-group">
        <input type="checkbox" id="recurring" name="recurring">
        <label for="recurring">Yes, I want a recurring subscription</label>
    </div>
    <button id="confirm-button">Next</button>
</div>

<div id="payment-form" class="hidden">
    <h2>Enter Payment Details</h2>
    <form id="stripe-form">
        <div class="form-group">
            <label for="card-element">Credit or debit card</label>
            <div id="card-element"></div>
        </div>
        <button type="submit">Pay</button>
    </form>
    <div id="paypal-button-container" class="hidden"></div>
    <p>Payments are handled through a secure connection with our third-party payments provider, Stripe. We never see or store your card details, ensuring your data remains private and secure.</p>
</div>

<script type="text/javascript" defer>
    // Extract price and days from the URL
    const urlParams = new URLSearchParams(window.location.search);
    const price = urlParams.get('price');
    const days = urlParams.get('days');
    const uid = urlParams.get('uid');

    Telegram.WebApp.ready();
    Telegram.WebApp.expand();

    // Display membership details
    document.getElementById('membership-details').textContent = `You will be charged $${(price / 100).toFixed(2)} for ${days} days of membership.`;
    document.getElementById('recurring-message').textContent = `Would you like to make this a recurring transaction, with automatic billing every ${days} days at $${(price / 100).toFixed(2)} USD? You can cancel at any time with no cost or penalty.`;

    document.getElementById('next-button').addEventListener('click', () => {
        document.getElementById('price-details').classList.add('hidden');
        document.getElementById('payment-methods').classList.remove('hidden');
    });

    //document.getElementById('pay-button').addEventListener('click', () => {
    //    document.getElementById('payment-methods').classList.add('hidden');
    //    document.getElementById('recurring-option').classList.remove('hidden');
    //});

    document.getElementById('pay-button').addEventListener('click', () => {
        const selectedPaymentMethod = document.querySelector('input[name="payment-method"]:checked').value;
        if (selectedPaymentMethod === 'stripe') {
            document.getElementById('payment-methods').classList.add('hidden');
            document.getElementById('payment-form').classList.remove('hidden');
            initializeStripe();
        } else if (selectedPaymentMethod === 'paypal') {
            document.getElementById('payment-methods').classList.add('hidden');
            document.getElementById('payment-form').classList.remove('hidden');
            initializePayPal();
        }
    });

    function initializeStripe() {
        const stripe = Stripe(urlParams.get('test') === '1' ? 'pk_test_51PG6qHL76pNLDU6DJfYrjnXhEPD05D2lAdHAGQF0VcmayFjRy1urPjgDjk2y6UdnzyGn078dYQgUiFMgTtRpz4ng00evAT4QEQ' : 'pk_live_51PG6qHL76pNLDU6DHbBB1bEGcjvaLtsz2uNQFdWZQkGmNA88V8VRovZHWFTDFQQmiZH9VPlc12c3zjLCzFNbtpAy00P1sUypNa');
        const elements = stripe.elements();
        const cardElement = elements.create('card', {
            style: {
                base: {
                    color: '#32325d',
                    fontFamily: 'Arial, sans-serif',
                    fontSmoothing: 'antialiased',
                    fontSize: '16px',
                    '::placeholder': {
                        color: '#aab7c4'
                    }
                },
                invalid: {
                    color: '#fa755a',
                    iconColor: '#fa755a'
                }
            }
        });
        cardElement.mount('#card-element');

        const form = document.getElementById('stripe-form');
        form.addEventListener('submit', async (event) => {
            event.preventDefault();

            const { token, error } = await stripe.createToken(cardElement);
            const errorElement = document.getElementById('payment-errors');
            errorElement.classList.add('hidden');

            if (error) {
                errorElement.textContent = error.message;
                errorElement.classList.remove('hidden');
            } else {
                document.getElementById('payment-form').style.display = 'none'; // Hide the card element
                document.getElementById('stripe-form').style.display = 'none'; // Hide the card element

                const websocketUrl = urlParams.get('test') === '1' ? 'wss://makefox.bot/wstest' : 'wss://makefox.bot/ws';
                const websocket = new WebSocket(websocketUrl);

                websocket.onopen = () => {
                    websocket.send(JSON.stringify({
                        "Command": "Payment:ProcessToken",
                        "Token": token.id,
                        "UID": uid,
                        "Price": price,
                        "Days": days
                    }));
                };

                websocket.onmessage = (event) => {
                    const response = JSON.parse(event.data);

                    if (response.Success) {
                        document.body.innerHTML = 'Success.';
                        Telegram.WebApp.close();
                    } else {
                        errorElement.textContent = response.Error;
                        errorElement.classList.remove('hidden');
                    }

                    websocket.close();
                };

                websocket.onerror = (err) => {
                    errorElement.textContent = 'WebSocket error: ' + err.message;
                    errorElement.classList.remove('hidden');
                };

                websocket.onclose = () => {
                    console.log('WebSocket connection closed');
                };
            }

            Telegram.WebApp.MainButton.setText('Close').show().onClick(function () {
                Telegram.WebApp.close();
            });
        });
    }

    function initializePayPal() {
        const priceFormatted = (price / 100).toFixed(2);
        paypal.Buttons({
            createOrder: (data, actions) => {
			return actions.order.create({
				purchase_units: [{
					amount: {
						value: priceFormatted
					}
				}],
				application_context: {
					shipping_preference: 'NO_SHIPPING' // Disable shipping address collection
				}
			});
            },
            onApprove: (data, actions) => {
                return actions.order.capture().then(details => {
                    processPayPalPayment(details);
                });
            },
            onError: (err) => {
                const errorElement = document.getElementById('payment-errors');
                errorElement.textContent = 'PayPal error: ' + err.message;
                errorElement.classList.remove('hidden');
            }
        }).render('#paypal-button-container');

        document.getElementById('paypal-button-container').classList.remove('hidden');
        document.getElementById('stripe-form').classList.add('hidden');
    }

    function processPayPalPayment(details) {
        const websocketUrl = urlParams.get('test') === '1' ? 'wss://makefox.bot/wstest' : 'wss://makefox.bot/ws';
        const websocket = new WebSocket(websocketUrl);

        websocket.onopen = () => {
            websocket.send(JSON.stringify({
                "Command": "Payment:ProcessPayPalOrder",
                "OrderID": details.id,
                "UID": uid,
                "Price": price,
                "Days": days
            }));
        };

        websocket.onmessage = (event) => {
            const response = JSON.parse(event.data);

            if (response.Success) {
                document.body.innerHTML = 'Success.';
                Telegram.WebApp.close();
            } else {
                const errorElement = document.getElementById('payment-errors');
                errorElement.textContent = response.Error;
                errorElement.classList.remove('hidden');
            }

            websocket.close();
        };

        websocket.onerror = (err) => {
            const errorElement = document.getElementById('payment-errors');
            errorElement.textContent = 'WebSocket error: ' + err.message;
            errorElement.classList.remove('hidden');
        };

        websocket.onclose = () => {
            console.log('WebSocket connection closed');
        };
    }
</script>

</body>
</html>
