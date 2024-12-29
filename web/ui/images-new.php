<?php

//-----------------------------------------

require_once ("../../lib/web/lib_default.php");
require_once ("../../lib/web/lib_login.php");

if (!checkUserLogin()) {
    exit;
}

if ($user['access_level'] == 'ADMIN' && isset($_GET['uid']) && is_numeric($_GET['uid'])) {
    $uid = (int) $_GET['uid'];
    if ($uid == -2) {
        $uid = $user['id'];
    }
} else {
    $uid = -1;
}

$dark = (isset($_GET['dark']) && $_GET['dark']);

if (isset($_GET['id']) && is_numeric($_GET['id']) && $_GET['id'] > 0) {
    $imageId = (int) $_GET['id'];
} else {
    $imageId = 0;
}

if (isset($_GET['model']) && strlen($_GET['model']) > 0) {
    $imageModel = htmlspecialchars($_GET['model'], ENT_QUOTES, 'UTF-8');
} else {
    $imageModel = "";
}

//-----------------------------------------

?>
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Image Viewer</title>
    <link rel="stylesheet" href="/css/<?php echo $dark ? 'dark.css' : 'main.css'; ?>">
    <script src="https://cdn.jsdelivr.net/npm/luxon"></script>
    <script src="/js/websocket.js"></script> <!-- Ensure this path is correct -->
    <style>
        /* Lightbox styles */
        #lightbox {
            display: none;
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: rgba(0,0,0,0.8);
            justify-content: center;
            align-items: center;
            z-index: 1000;
            overflow: auto;
        }
        #lightbox .image-container {
            position: relative;
            max-width: 90%;
            max-height: 90%;
        }
        #lightbox img {
            width: 100%;
            height: auto;
            border-radius: 5px;
        }
        #lightbox .close-btn {
            position: absolute;
            top: 10px;
            right: 20px;
            font-size: 2em;
            color: white;
            cursor: pointer;
        }

        /* Container using Flexbox */
        #imageContainer {
            display: flex;
            flex-wrap: wrap;
            justify-content: flex-start; /* Align items to the start */
            align-items: stretch; /* Stretch items vertically to match the tallest */
            padding: 10px;
            gap: 10px; /* Space between items */
        }

        .image-wrapper {
            width: 200px; /* Fixed column width */
            display: flex;
            flex-direction: column;
            box-sizing: border-box;
        }

        .image-wrapper img {
            width: 100%;
            height: auto;
            border-radius: 5px;
            transition: transform 0.2s;
            flex-shrink: 0; /* Prevent shrinking */
        }

        .image-wrapper img:hover {
            transform: scale(1.05);
        }

        .caption {
            padding: 5px 0;
            font-size: 0.9em;
            color: #333;
            flex-grow: 1; /* Push caption to take available space */
        }

        .shorten {
            cursor: pointer;
            color: #007BFF;
        }

        .shorten.active {
            color: #0056b3;
        }

        /* Loading and error message styles */
        #loading {
            text-align: center;
            padding: 20px;
            font-size: 1.2em;
            color: #555;
            display: none; /* Initially hidden */
            width: 100%;
        }
        #error-message {
            color: red;
            text-align: center;
            display: none;
            width: 100%;
        }

        /* Responsive Adjustments */
        @media (max-width: 1200px) {
            .image-wrapper {
                width: 150px; /* Adjust column width for medium screens */
            }
        }
        @media (max-width: 800px) {
            .image-wrapper {
                width: 120px; /* Adjust column width for small screens */
            }
        }
        @media (max-width: 500px) {
            .image-wrapper {
                width: 100%; /* Single column on very small screens */
            }
        }
    </style>
</head>
<body>
    <div id="imageContainer"></div>
    <div id="loading">Loading more images...</div>
    <div id="error-message"></div>

    <div id="lightbox"></div>

    <script>
        // Initialize variables
        let lastImageId = 0;
        let highestImageId = 0;
        let isLoading = false;
        let hasMoreOldImages = true;

        let imagesData = {};

        let currentImageIdx = null; // Global variable to track the current image index

        <?php if (!$imageId) { ?>
            document.addEventListener('DOMContentLoaded', () => {
                console.log("DOM fully loaded and parsed.");

                createLightbox(); // Initialize the lightbox
                setupLightboxScroll(); // Setup lightbox navigation
                setupInfiniteScroll(); // Setup infinite scrolling

                // Initial fetch for 'old' images
                fetchAndRenderQueue('old');

                // Inside DOMContentLoaded event
                // Initialize the timer to check for new images at the top
                //setInterval(checkForNewImages, 800);
            });
        <?php } ?>

        <?php if ($imageId) { ?>
            document.addEventListener('DOMContentLoaded', () => {
                console.log("DOM fully loaded and parsed with specific image ID.");
                createLightbox(false); // Initialize the lightbox without close functionality
                showOneImage(<?php echo $imageId; ?>); // Display the specific image
            });
        <?php } ?>

        /**
         * Create the lightbox element
         */
        function createLightbox(closeable = true) {
            console.log("Creating lightbox. Closeable:", closeable);
            const lightbox = document.getElementById('lightbox');
            lightbox.innerHTML = ''; // Clear any existing content

            if (closeable) {
                const closeButton = document.createElement('div');
                closeButton.classList.add('close-btn');
                closeButton.innerHTML = '&times;';
                lightbox.appendChild(closeButton);

                closeButton.addEventListener('click', (e) => {
                    e.stopPropagation();
                    lightbox.style.display = 'none';
                    document.body.style.overflow = '';
                    console.log("Lightbox closed.");
                });

                lightbox.addEventListener('click', () => {
                    lightbox.style.display = 'none';
                    document.body.style.overflow = '';
                    console.log("Lightbox background clicked and closed.");
                });
            }
        }

        /**
         * Setup Lightbox Scroll for navigation
         */
        function setupLightboxScroll() {
            console.log("Setting up lightbox scroll navigation.");
            const lightbox = document.getElementById('lightbox');

            lightbox.addEventListener('wheel', (e) => {
                e.preventDefault(); // Prevent the page from scrolling

                if (e.deltaY < 0) {
                    // Scrolling up, show next image
                    console.log("Wheel scrolled up.");
                    showNextImage();
                } else if (e.deltaY > 0) {
                    // Scrolling down, show previous image
                    console.log("Wheel scrolled down.");
                    showPreviousImage();
                }
            });
        }

        /**
         * Fetch the image URL using PHP endpoint
         */
        function fetchImageUrl(imageId) {
            console.log(`Fetching image URL for ID: ${imageId}`);
            return `/api/get-image.php?id=${imageId}`;
        }

        /**
         * Shorten text utility
         */
        function shortenText(text, maxLength) {
            const isTextShortened = text.length > maxLength;
            const shortenedText = isTextShortened ? text.substring(0, maxLength - 3) + "..." : text;
            return { shortenedText, isTextShortened };
        }

        /**
         * Generate the caption HTML for an image
         */
        function generateCaption(image, shortenChars = 20) {
            let q = image;

            let { shortenedText: promptShortened, isTextShortened: promptShortenedFlag } = shortenText(q.Prompt, shortenChars);
            let { shortenedText: negativeShortened, isTextShortened: negativeShortenedFlag } = shortenText(q.NegativePrompt, shortenChars);

            // Using Luxon to handle date formatting
            const { DateTime } = luxon;
            const serverTime = DateTime.fromISO(q.DateCreated, { zone: 'America/Chicago' });
            const dateAdded = serverTime.setZone(DateTime.local().zoneName);

            let caption =
                `<div><strong>ID:</strong> <a href="?id=${q.ID}">${q.ID}</a><br></div>` +
                `<?php if ($user['access_level'] == 'ADMIN'): ?>` +
                `<div><strong>User:</strong> <a href="?uid=${q.UID}">${q.Username ? q.Username : '(' + (q.Firstname || '') + (q.Firstname && q.Lastname ? ' ' : '') + (q.Lastname || '') + ')'}</a><br></div>` +
                `<?php endif; ?>` +
                `<div>${q.TeleChatID == q.TeleID ? "" : '<strong>Chat:</strong> ' + q.TeleChatID + '<br>'}</div>` +
                `${q.Prompt ? `<div><strong>Prompt:</strong> <span class="${promptShortenedFlag ? 'shorten' : ''}" ${promptShortenedFlag ? `data-fulltext="${escapeHTML(q.Prompt)}" data-shorttext="${escapeHTML(promptShortened)}"` : ''}>${escapeHTML(promptShortened)}</span><br></div>` : ''}` +
                `${q.NegativePrompt ? `<div><strong>Negative:</strong> <span class="${negativeShortenedFlag ? 'shorten' : ''}" ${negativeShortenedFlag ? `data-fulltext="${escapeHTML(q.NegativePrompt)}" data-shorttext="${escapeHTML(negativeShortened)}"` : ''}>${escapeHTML(negativeShortened)}</span><br></div>` : ''}` +
                `${q.HiresEnabled ? `<div><strong>Size:</strong> ${q.HiresWidth}x${q.HiresHeight} (from ${q.Width}x${q.Height}) <br></div>` : `<div><strong>Size:</strong> ${q.Width}x${q.Height}<br></div>`}` +
                `<div><strong>Sampler Steps:</strong> ${q.Steps}<br></div>` +
                `<div><strong>CFG Scale:</strong> ${q.CFGScale}<br></div>` +
                `${q.Type === 'IMG2IMG' ? `<div><strong>Denoising Strength:</strong> ${q.DenoisingStrength}<br></div>` : ''}` +
                `<div><strong>Model:</strong> <a href="?uid=<?php echo $uid; ?>&model=${encodeURIComponent(q.Model)}">${escapeHTML(q.Model)}</a><br></div>` +
                `<div><strong>Seed:</strong> ${q.Seed}<br></div>` +
                `<div><strong>Sampler:</strong> ${escapeHTML(q.Sampler)}<br></div>` +
                `<?php if ($user['access_level'] == 'ADMIN'): ?>` +
                `<div><strong>Worker:</strong> ${escapeHTML(q.WorkerName)}<br></div>` +
                `<?php endif; ?>` +
                `<div>${escapeHTML(dateAdded.toFormat('dd LLL yyyy hh:mm:ss a ZZZZ'))}</div>`; // Appending the formatted timestamp with timezone

            return caption;
        }

        /**
         * Escape HTML to prevent XSS
         */
        function escapeHTML(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }

        /**
         * Setup click to expand shortened text
         */
        function setupShortenForElement(element) {
            element.querySelectorAll('.shorten').forEach(span => {
                // Only add click event if the text was shortened
                if (span.dataset.shorttext && span.dataset.fulltext && span.dataset.shorttext !== span.dataset.fulltext) {
                    span.classList.add('can-expand'); // Add a class to indicate it can be expanded
                    span.onclick = function() {
                        if (this.classList.contains('active')) {
                            this.classList.remove('active');
                            this.textContent = this.getAttribute('data-shorttext');
                            console.log("Shortened text collapsed.");
                        } else {
                            this.classList.add('active');
                            this.textContent = this.getAttribute('data-fulltext');
                            console.log("Shortened text expanded.");
                        }
                    };
                }
            });
        }

        /**
         * Display images on the page
         */
        function displayImages(images, action) {
            console.log(`Displaying images with action: ${action}`);
            const container = document.getElementById('imageContainer');
            let newImagesCount = 0;

            images.forEach((image) => {
                const idx = image.ID; // Assuming 'ID' is the unique identifier

                // Avoid duplicates
                if (imagesData[idx]) {
                    console.log(`Image ID ${idx} already exists. Skipping.`);
                    return;
                }

                imagesData[idx] = image; // Store the image data
                newImagesCount++;
                console.log(`Displaying Image ID: ${idx}`);

                const wrapper = document.createElement('div');
                wrapper.classList.add('image-wrapper');

                // Primary Image Element
                const primaryImgElement = document.createElement('img');
                primaryImgElement.src = fetchImageUrl(image.ImageID);
                primaryImgElement.loading = 'lazy'; // Enable lazy loading
                primaryImgElement.setAttribute('data-image-id', idx); // Set the image ID as a data attribute
                primaryImgElement.addEventListener('click', () => handleImageClick(idx)); // Add click listener
                wrapper.appendChild(primaryImgElement);
                console.log(`Added primary image: ${image.ImageID}`);

                // Secondary Image for IMG2IMG type
                if (image.Type === "IMG2IMG" && image.SelectedImage) {
                    const secondaryImgElement = document.createElement('img');
                    secondaryImgElement.src = fetchImageUrl(image.SelectedImage);
                    secondaryImgElement.setAttribute('data-image-id', idx); // Set the image ID as a data attribute
                    secondaryImgElement.addEventListener('click', () => handleImageClick(idx)); // Add click listener
                    secondaryImgElement.loading = 'lazy'; // Enable lazy loading
                    wrapper.appendChild(secondaryImgElement);
                    console.log(`Added secondary image for IMG2IMG type: ${image.SelectedImage}`);
                }

                const textElement = document.createElement('div');
                textElement.className = 'caption';
                textElement.innerHTML = generateCaption(image, 400);

                setupShortenForElement(textElement);

                wrapper.appendChild(textElement);

                if (action === 'new') {
                    container.insertBefore(wrapper, container.firstChild);
                    console.log(`Prepended Image ID: ${idx}`);
                } else if (action === 'old') {
                    container.appendChild(wrapper);
                    console.log(`Appended Image ID: ${idx}`);
                }
            });

            updateImageIds(images, action);

            // Return the number of new images added
            return newImagesCount;
        }

        /**
         * Update the last and highest image IDs for pagination
         */
        function updateImageIds(images, action) {
            if (action === 'new') {
                const maxID = Math.max(...images.map(img => img.ID));
                if (maxID > highestImageId) {
                    highestImageId = maxID;
                    console.log(`Updated highestImageId to: ${highestImageId}`);
                }
            } else if (action === 'old') {
                const minID = Math.min(...images.map(img => img.ID));
                lastImageId = minID - 1; // Set to one less than the smallest ID fetched
                console.log(`Updated lastImageId to: ${lastImageId}`);
            }
        }

        /**
         * Handle image click to show in lightbox
         */
        function handleImageClick(idx) {
            console.log(`Image clicked: ${idx}`);
            const image = imagesData[idx];
            if (!image) {
                console.error("Image data not found for idx:", idx);
                return;
            }

            showLightbox(idx); // Pass idx as well
        }

        /**
         * Show image in lightbox
         */
        function showLightbox(idx) {
            console.log(`Showing lightbox for Image ID: ${idx}`);
            const lightbox = document.getElementById('lightbox');
            lightbox.innerHTML = ''; // Clear previous content

            createLightbox(); // Re-create lightbox with close functionality

            currentImageIdx = idx; // Update the global variable with the current index

            const image = imagesData[idx];
            if (!image) {
                console.error("Image data not found for idx:", idx);
                return;
            }

            const captionHTML = generateCaption(image, 400);

            const imageContainer = document.createElement('div');
            imageContainer.classList.add('image-container'); // Use class for styling

            const img = document.createElement('img');
            img.src = fetchImageUrl(image.ImageID) + '&full=1';
            img.alt = "Full size image";
            img.loading = 'lazy'; // Enable lazy loading
            imageContainer.appendChild(img);

            const captionDiv = document.createElement('div');
            captionDiv.innerHTML = captionHTML;
            captionDiv.classList.add('caption'); // Use class for styling
            captionDiv.addEventListener('click', (e) => e.stopPropagation());

            setupShortenForElement(captionDiv);

            img.onload = function () {
                console.log(`Full-size image loaded: ${image.ImageID}`);
                if (this.naturalWidth < window.innerWidth && this.naturalHeight < window.innerHeight) {
                    this.style.width = Math.min(this.naturalWidth, window.innerWidth * 0.9) + 'px';
                    this.style.height = 'auto';
                    console.log("Adjusted image size based on natural dimensions.");
                }
                let totalHeight = this.offsetHeight + captionDiv.offsetHeight;
                if (window.innerHeight > totalHeight) {
                    imageContainer.appendChild(captionDiv);
                    console.log("Appended caption to image container based on height.");
                }
            };

            imageContainer.appendChild(captionDiv);
            lightbox.appendChild(imageContainer);
            lightbox.style.display = 'flex';
            document.body.style.overflow = 'hidden';
            console.log("Lightbox displayed.");
        }

        /**
         * Show Previous Image in Lightbox
         */
        function showPreviousImage() {
            const keys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
            const currentIndex = keys.indexOf(Number(currentImageIdx));
            if (currentIndex > 0) {
                const previousIndex = keys[currentIndex - 1];
                const previousImage = imagesData[previousIndex];
                if (previousImage) {
                    showLightbox(previousIndex.toString());
                }
            } else {
                fetchAndRenderQueue('old').then(() => {
                    const updatedKeys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
                    const newCurrentIndex = updatedKeys.indexOf(Number(currentImageIdx));
                    if (newCurrentIndex > 0) {
                        showPreviousImage(); // Try showing the previous image again
                    }
                }).catch(error => console.error(error));
            }
        }

        /**
         * Show Next Image in Lightbox
         */
        function showNextImage() {
            const keys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
            const currentIndex = keys.indexOf(Number(currentImageIdx));
            if (currentIndex < keys.length - 1) {
                const nextIndex = keys[currentIndex + 1];
                const nextImage = imagesData[nextIndex];
                if (nextImage) {
                    showLightbox(nextIndex.toString());
                }
            } else {
                fetchAndRenderQueue('new').then(() => {
                    const updatedKeys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
                    const newCurrentIndex = updatedKeys.indexOf(Number(currentImageIdx));
                    if (newCurrentIndex < updatedKeys.length - 1) {
                        showNextImage(); // Try showing the next image again
                    }
                }).catch(error => console.error(error));
            }
        }

        /**
         * Show a single image (used when ?id is set)
         */
        function showOneImage(idx) {
            console.log(`Fetching single image with ID: ${idx}`);
            return ListQueue({
                ID: parseInt(idx, 10), // Pass only the specific ID
                PageSize: 1 // Fetch only one image
            }).then(queueItems => {
                console.log(`Received ${queueItems.length} queue items for single image.`);
                if (queueItems.length > 0) {
                    const image = queueItems[0]; // Since PageSize is 1
                    imagesData[image.ID] = image;
                    displayImages([image], 'new'); // Pass as an array
                    showLightbox(image.ID);
                    console.log(`Single image fetched and displayed: ${image.ID}`);
                    return image;
                } else {
                    console.log('No images returned for the specific ID.');
                    throw 'No images returned';
                }
            }).catch(error => {
                console.error('Error while fetching single image:', error);
            });
        }

        /**
         * Fetch and render queue data using WebSocket (from websocket.js)
         * @param {string} action - 'new' or 'old'
         */
        function fetchAndRenderQueue(action = 'old') {
            if (isLoading) {
                console.log("Already loading. Skipping fetch.");
                return;
            }
            isLoading = true;
            document.getElementById('loading').style.display = 'block';
            document.getElementById('error-message').style.display = 'none';
            console.log(`Fetching and rendering queue with action: ${action}`);

            let modelFilter = '<?php echo $imageModel; ?>';
            let statusFilter = 'FINISHED'; // Adjust as needed

            let requestParams = {
                UID: <?php echo $uid; ?>,
                action: action, // 'new' or 'old'
                PageSize: 20,
                Status: statusFilter,
                Model: modelFilter
            };

            if (action === 'old' && lastImageId > 0) {
                requestParams.lastImageId = lastImageId;
            } else if (action === 'new' && highestImageId > 0) {
                requestParams.lastImageId = highestImageId;
            }

            console.log("ListQueue request parameters:", requestParams);

            // Assuming ListQueue is defined in websocket.js and returns a Promise
            ListQueue(requestParams).then(queueItems => {
                console.log(`Received ${queueItems.length} queue items.`);
                if (queueItems.length > 0) {
                    const newImagesCount = displayImages(queueItems, action); // Pass the array directly

                    if (action === 'old') {
                        // Set lastImageId to min ID - 1
                        const minID = Math.min(...queueItems.map(img => img.ID));
                        lastImageId = minID - 1;
                        console.log(`Updated lastImageId to: ${lastImageId}`);
                    } else if (action === 'new') {
                        const maxID = Math.max(...queueItems.map(img => img.ID));
                        highestImageId = maxID;
                        console.log(`Updated highestImageId to: ${highestImageId}`);
                    }

                    // If no new images were added, stop further fetching
                    if (action === 'old' && newImagesCount === 0) {
                        hasMoreOldImages = false;
                        console.log("No more old images available.");
                    }
                } else {
                    console.log(`No more images to fetch for action: ${action}`);
                    if (action === 'old') {
                        hasMoreOldImages = false;
                        console.log("No more old images available.");
                    }
                }
                document.getElementById('loading').style.display = 'none';
                isLoading = false;
            }).catch(error => {
                console.error('WebSocket error:', error);
                document.getElementById('loading').style.display = 'none';
                document.getElementById('error-message').textContent = 'Failed to load queue data.';
                document.getElementById('error-message').style.display = 'block';
                isLoading = false;
            });
        }

        /**
         * Setup Infinite Scrolling
         */
        function setupInfiniteScroll() {
            console.log("Setting up infinite scroll.");
            window.addEventListener('scroll', debounce(handleScroll, 100));
            console.log("Infinite scroll event listener added.");
        }

        /**
         * Handle scroll events for infinite scrolling
         */
        function handleScroll() {
            const scrollTop = window.scrollY;
            const windowHeight = window.innerHeight;
            const documentHeight = document.body.offsetHeight;

            // When the user scrolls near the bottom (e.g., within 100px), load more 'old' images
            if (scrollTop + windowHeight >= documentHeight - 100 && !isLoading && hasMoreOldImages) {
                console.log("Near bottom of the page. Fetching more 'old' images...");
                fetchAndRenderQueue('old');
            }

            // When the user scrolls near the top (e.g., within 100px), load 'new' images
            if (scrollTop <= 100 && !isLoading) {
                console.log("Near top of the page. Fetching 'new' images...");
                fetchAndRenderQueue('new');
            }
        }

        /**
         * Debounce function to limit the rate of function calls
         */
        function debounce(func, wait) {
            let timeout;
            return function(...args) {
                clearTimeout(timeout);
                timeout = setTimeout(() => func.apply(this, args), wait);
            };
        }

        /**
         * Timer function to periodically check for new images at the top
         */
        function checkForNewImages() {
            const scrollTop = window.scrollY;
            if (scrollTop <= 100 && !isLoading) { // Check if scrolled to the very top or within 100px
                console.log("Timer check: User is at the top. Fetching 'new' images...");
                fetchAndRenderQueue('new');
            } else {
                console.log("Timer check: User is not at the top. No action taken.");
            }
        }



    </script>
</body>
</html>