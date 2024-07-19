<?php
require_once("../../lib/web/lib_default.php");

$ppEnabled = isset($config['paypal']['client_id']);
$stripeEnabled = isset($config['stripe']['public_key']);

if (!isset($config['web']['websocket_url'])) {
    die('WebSocket URL is not set in the configuration file.');
}

if (!isset($_GET['id'])) {
    die('Missing payment session ID.');
}

$uuid = $_GET['id'];

$pdo = new PDO("mysql:host=" . MYSQL_HOST . ";dbname=" . MYSQL_DBNAME . ";charset=utf8mb4", MYSQL_USERNAME, MYSQL_PASSWORD);
$pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

$stmt = $pdo->prepare("SELECT * FROM payment_sessions WHERE uuid = :uuid");
$stmt->execute(['uuid' => $uuid]);

$s = $stmt->fetch();

if (!isset($s) || !isset($s['date_created'])) {
    die('Invalid payment session ID.');
}

$dateCreated = strtotime($s['date_created']);

if ((time() - $dateCreated) > 3600) { // 3600 seconds = 1 hour
    die('Payment link has expired.  Please try again.');
}

if (!isset($s['user_id'])) {
    die('Invalid payment session data.');
}

if (isset($_GET['amount']))
    $amount = (int) $_GET['amount'];
else
    $amount = 0;

$uid = $s['user_id'];
$currency = "USD";

?>

<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Membership Purchase</title>
    <script src="https://telegram.org/js/telegram-web-app.js"></script>
    <?php if ($stripeEnabled) { ?>
        <script src="https://js.stripe.com/v3/"></script>
    <?php } ?>
    <?php if ($ppEnabled) { ?>
        <script src="https://www.paypal.com/sdk/js?client-id=<?php echo $config['paypal']['client_id']; ?>&disable-funding=credit,card&intent=authorize&commit=true"></script>
    <?php } ?>
    <script src="/js/websocket.js"></script>
    <style>
        #main-content {
            display: none;
        }

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

        #spinner img {
            width: 50px; /* Set the desired width */
            height: auto; /* Maintain aspect ratio */
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
    <div id="spinner" style="text-align: center; margin-top: 20px;">
        <img src="/img/spinner.gif" alt="Connecting..." />
        <p>Connecting to server...</p>
    </div>

    <div id="payment-errors" class="hidden" style="color: red; margin-top: 20px;"></div>

    <div id="main-content">

        <div id="amount-step" class="hidden">
            <h1>Enter Amount</h1>
            <input type="number" id="amount-input" placeholder="Enter amount (min $5.00)" min="5" />
            <p id="reward-days" class="hidden"></p>
            <button id="amount-next-button">Next</button>
        </div>

        <div id="confirm-details" class="hidden">
            <h1>Confirm Membership Purchase</h1>
            <p id="confirm-details-text"></p>
            <button id="confirm-button">Confirm</button>
        </div>

        <!-- <div id="recurring-option" class="hidden">
            <h2>Recurring Membership</h2>
            <p id="recurring-message"></p>
            <div class="form-group">
                <input type="checkbox" id="recurring" name="recurring" />
                <label for="recurring">Yes, I want a recurring subscription</label>
            </div>
            <button id="confirm-recurring-button">Next</button>
        </div> -->

        <div id="payment-methods" class="hidden">
            <h2>Select Payment Method</h2>
            <?php if ($stripeEnabled) { ?>
                <div class="form-group">
                    <input type="radio" id="stripe" name="payment-method" value="stripe" checked />
                    <label for="stripe">Stripe (Credit Card)</label>
                </div>
            <?php } ?>
            <?php if ($ppEnabled) { ?>
                <div class="form-group">
                    <input type="radio" id="paypal" name="payment-method" value="paypal" />
                    <label for="paypal">PayPal</label>
                </div>
            <?php } ?>
            <?php if (!$stripeEnabled && !$ppEnabled) { ?>
                <p style="color: red;">No payment methods are available at this time.</p>
            <?php } ?>
            <button id="pay-button">Next</button>
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

            function sleep(ms) {
                return new Promise(resolve => setTimeout(resolve, ms));
            }

            document.addEventListener('DOMContentLoaded', async function () {
                document.getElementById('spinner').style.display = 'block';
                document.getElementById('main-content').style.display = 'none';
                await ensureWebSocketOpen();
                document.getElementById('spinner').style.display = 'none';
                document.getElementById('main-content').style.display = 'block';
                console.log("Starting page...");

                let price = <?php echo $amount; ?>;
                const uuid = '<?php echo $uuid; ?>';
                const uid = <?php echo $uid; ?>;
                const currency = '<?php echo $currency; ?>';
                let days;

                if (price <= 0) {
                    // Add input field for user to enter the amount
                    document.getElementById('amount-step').classList.remove('hidden');
                    const inputField = document.getElementById('amount-input');
                    const rewardDaysP = document.getElementById('reward-days');
                    rewardDaysP.classList.remove('hidden');

                    inputField.addEventListener('input', async () => {
                        const amount = parseFloat(inputField.value);
                        if (amount >= 5) {
                            days = await CalcRewardDays(amount * 100);
                            rewardDaysP.textContent = `You will get ${days} days of membership for $${amount.toFixed(2)}.`;
                            price = amount * 100;
                        } else {
                            rewardDaysP.textContent = 'Amount must be at least $5.00.';
                        }
                    });

                    document.getElementById('amount-next-button').addEventListener('click', () => {
                        if (price >= 500) {
                            document.getElementById('confirm-details-text').textContent = `You will be charged $${(price / 100).toFixed(2)} ${currency} for ${days} days of membership. Would you like to proceed?`;
                            document.getElementById('amount-step').classList.add('hidden');
                            document.getElementById('confirm-details').classList.remove('hidden');
                        } else {
                            alert('Amount must be at least $5.00.');
                        }
                    });
                } else {
                    days = await CalcRewardDays(price);
                    document.getElementById('confirm-details-text').textContent = `You will be charged $${(price / 100).toFixed(2)} ${currency} for ${days} days of membership. Would you like to proceed?`;
                    document.getElementById('confirm-details').classList.remove('hidden');
                }

                document.getElementById('confirm-button').addEventListener('click', () => {
                    document.getElementById('confirm-details').classList.add('hidden');
                    // document.getElementById('recurring-option').classList.remove('hidden'); // Uncomment this line to enable recurring step
                    document.getElementById('payment-methods').classList.remove('hidden'); // Remove this line to enable recurring step
                });

                // document.getElementById('confirm-recurring-button').addEventListener('click', () => {
                //     document.getElementById('recurring-option').classList.add('hidden');
                //     document.getElementById('payment-methods').classList.remove('hidden');
                // });

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
                    const stripe = Stripe('<?php echo ($config['stripe']['public_key']); ?>');
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

                        const errorElement = document.getElementById('payment-errors');
                        errorElement.classList.add('hidden');

                        try {
                            const { token, error } = await stripe.createToken(cardElement);

                            if (error) {
                                // Handle Stripe token creation error
                                console.error('Stripe token creation error:', error);
                                errorElement.textContent = error.message;
                                errorElement.classList.remove('hidden');
                                return;
                            }

                            document.getElementById('payment-form').style.display = 'none'; // Hide the card element
                            document.getElementById('stripe-form').style.display = 'none'; // Hide the card element

                            try {
                                console.log('Processing payment with token:', token.id);
                                await ProcessPayment(uuid, "Stripe", price, token.id);
                                document.body.innerHTML = 'Success.';
                                Telegram.WebApp.close();
                            } catch (err) {
                                // Handle errors from ProcessPayment
                                console.error('Error processing payment:', err);
                                errorElement.textContent = err.message || 'An error occurred while processing your payment.';
                                errorElement.classList.remove('hidden');
                                document.getElementById('payment-form').style.display = 'block'; // Show the card element
                                document.getElementById('stripe-form').style.display = 'block'; // Show the stripe form
                            }
                        } catch (generalError) {
                            // Handle any other errors
                            console.error('General error:', generalError);
                            errorElement.textContent = generalError.message || 'An unexpected error occurred.';
                            errorElement.classList.remove('hidden');
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
                                },
                                intent: 'AUTHORIZE' // Change intent to authorize
                            });
                        },
                        onApprove: async (data, actions) => {
                            const errorElement = document.getElementById('payment-errors');
                            try {
                                const authorization = await actions.order.authorize();
                                const authorizationId = authorization.purchase_units[0].payments.authorizations[0].id;

                                console.log(authorization);

                                // Send the authorizationId to the backend via WebSocket
                                if (await ProcessPayment(uuid, "PayPal", price, authorizationId)) {
                                    document.body.innerHTML = 'Success.';
                                    Telegram.WebApp.close();
                                }
                            } catch (err) {
                                errorElement.textContent = err.message || 'An error occurred during the PayPal authorization.';
                                errorElement.classList.remove('hidden');
                                document.getElementById('payment-form').style.display = 'block'; // Show the card element
                                document.getElementById('stripe-form').style.display = 'block'; // Show the stripe form
                            }
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


            });


        </script>
    </div>
</body>
</html>
