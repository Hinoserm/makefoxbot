<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <script src="https://telegram.org/js/telegram-web-app.js"></script>
  <script src="/js/websocket.js"></script>
  <title>Styles List</title>
  <link rel="stylesheet" href="/tgapp/styles.css">
</head>
<body>

<div id="spinner" style="text-align: center; margin-top: 20px;">
    <img src="/img/spinner.gif" alt="Connecting..." />
    <p>Connecting to server...</p>
</div>

  <div id="main-content" class="container">
    <div id="styles-list"></div>
    <button id="add-new-btn" class="add-new-btn">Add New Style</button>
    <div id="error-msg" class="error-msg"></div>

    <!-- Template for Style Item -->
    <template id="style-template">
      <div class="style-item">
        <div class="style-header">
          <span class="grab-handle">≡</span>
          <button class="collapse-btn">▼</button>
          <span class="name"></span>
          <span class="toggle-enabled">❌</span>
          <button class="delete-btn">🗑️</button>
        </div>
        <div class="style-content" style="display: none;">
          <label>Name: <input type="text" class="style-name"></label>
          <label>Prompt: <textarea class="style-prompt"></textarea></label>
          <label>Negative Prompt: <textarea class="style-negative-prompt"></textarea></label>
          <div class="save-and-enabled-container">
            <button class="save-btn">Save</button>
            <label class="enabled-checkbox">
              <input type="checkbox" class="enabled-toggle"> Enabled
            </label>
          </div>
        </div>
      </div>
    </template>

    <!-- Modal for adding new style -->
    <div id="add-style-modal" class="modal">
      <div class="modal-content">
        <span class="close-btn">&times;</span>
        <h2>Add New Style</h2>
        <label>Name: <input type="text" id="new-style-name" placeholder="Style Name"></label>
        <label>Prompt: <textarea id="new-style-prompt" placeholder="Enter prompt"></textarea></label>
        <label>Negative Prompt: <textarea id="new-style-negative-prompt" placeholder="Enter negative prompt"></textarea></label>
        <button id="save-new-style-btn">Save</button>
        <div id="modal-error-msg" class="error-msg"></div>
      </div>
    </div>

    <!-- Delete Confirmation Modal -->
    <div id="delete-style-modal" class="modal">
      <div class="modal-content">
        <p id="delete-modal-text"></p>
        <button id="confirm-delete-btn">Confirm</button>
        <button id="cancel-delete-btn">Cancel</button>
      </div>
    </div>
  </div>

  <script src="/tgapp/styles.js"></script>
</body>
</html>
