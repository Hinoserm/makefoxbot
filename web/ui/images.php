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
    <script src="/js/websocket.js"></script>
    <style>
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

        #imageContainer {
            display: flex;
            flex-wrap: wrap;
            justify-content: flex-start;
            align-items: stretch;
            padding: 10px;
            gap: 10px;
        }

        .image-wrapper {
            width: 200px;
            display: flex;
            flex-direction: column;
            box-sizing: border-box;
            overflow: hidden;
            position: relative;
        }

        .image-wrapper img {
            width: 100%;
            height: auto;
            border-radius: 5px;
            transition: transform 0.2s;
            flex-shrink: 0;
            background-color: #f0f0f0;
        }

        .image-wrapper img.placeholder {
            background-color: #e0e0e0;
        }

        .image-wrapper img:hover {
            transform: scale(1.05);
        }

        .caption {
            padding: 5px 0;
            font-size: 0.9em;
            color: #333;
            flex-grow: 1;
        }

        .shorten {
            cursor: pointer;
            color: #007BFF;
        }

        .shorten.active {
            color: #0056b3;
        }

        #loading {
            text-align: center;
            padding: 20px;
            font-size: 1.2em;
            color: #555;
            display: none;
            width: 100%;
        }
        #error-message {
            color: red;
            text-align: center;
            display: none;
            width: 100%;
        }

        @media (max-width: 1200px) {
            .image-wrapper {
                width: 150px;
            }
        }
        @media (max-width: 800px) {
            .image-wrapper {
                width: 120px;
            }
        }
        @media (max-width: 500px) {
            .image-wrapper {
                width: 100%;
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
        let lastImageId = 0;
        let highestImageId = 0;
        let isLoading = false;
        let hasMoreOldImages = true;
        let totalImagesLoaded = 0;
        let imagesData = {};
        let currentImageIdx = null;

        const IMAGE_WRAPPER_WIDTH = 200;
        const IMAGE_WRAPPER_HEIGHT_ESTIMATE = 250;

        /**
         * Now we load for "2 screens" total (1 above + 1 below).
         * This is smaller than the "4" or "8-10 rows" you disliked.
         */
        function calculateRequiredImages() {
            const windowWidth = window.innerWidth;
            const windowHeight = window.innerHeight;
            const gap = 10;

            const columns = Math.floor((windowWidth - 20) / (IMAGE_WRAPPER_WIDTH + gap));
            if (columns < 1) return 20;

            // We'll do 2 screen heights total
            const rows = Math.ceil((windowHeight * 2) / (IMAGE_WRAPPER_HEIGHT_ESTIMATE + gap));
            let totalImages = columns * rows;
            totalImages = Math.max(20, totalImages);
            return totalImages;
        }

        function handleResize() {
            const requiredImages = calculateRequiredImages();
            if (requiredImages > 0) {
                fetchAndRenderQueue('old', requiredImages).then(() => {
                    checkAndLoadMoreImages();
                    unloadDistantImages();
                });
            }
        }

        function checkAndLoadMoreImages() {
            const container = document.getElementById('imageContainer');
            const containerHeight = container.offsetHeight;
            // We'll keep 1 screen buffer
            const needed = window.innerHeight;
            if (containerHeight < needed && hasMoreOldImages) {
                const more = calculateRequiredImages();
                if (more > 0) {
                    fetchAndRenderQueue('old', more).then(() => {
                        checkAndLoadMoreImages();
                    });
                }
            }
        }

        /**
         * We'll remove the actual image src if more than 1 screen away in either direction
         */
        function unloadDistantImages() {
            const container = document.getElementById('imageContainer');
            const wrappers = Array.from(container.children);

            const screenHeight = window.innerHeight;
            const bufferDist = 1 * screenHeight; // keep 1 screen in either direction

            wrappers.forEach(wrapper => {
                const rect = wrapper.getBoundingClientRect();
                const distAbove = -rect.bottom; 
                const distBelow = rect.top - window.innerHeight;

                if (distAbove > bufferDist || distBelow > bufferDist) {
                    // Unload images, keep wrapper
                    const images = wrapper.querySelectorAll('img');
                    images.forEach(img => {
                        if (!img.dataset.unloaded) {
                            console.log(`Unloading image for ID: ${img.getAttribute('data-image-id')}`);
                            img.dataset.origsrc = img.src;
                            img.src = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==';
                            img.classList.add('placeholder');
                            img.dataset.unloaded = 'true';
                        }
                    });
                } else {
                    // If we scrolled back, reload if unloaded
                    const images = wrapper.querySelectorAll('img');
                    images.forEach(img => {
                        if (img.dataset.unloaded === 'true' && img.dataset.origsrc) {
                            console.log(`Reloading image for ID: ${img.getAttribute('data-image-id')}`);
                            img.src = img.dataset.origsrc;
                            img.classList.remove('placeholder');
                            img.dataset.unloaded = '';
                        }
                    });
                }
            });
        }

        <?php if (!$imageId) { ?>
        document.addEventListener('DOMContentLoaded', () => {
            createLightbox();
            setupLightboxScroll();
            setupInfiniteScroll();

            const initialImages = calculateRequiredImages();
            fetchAndRenderQueue('old', initialImages).then(() => {
                checkAndLoadMoreImages();
            });

            // This is the timer that periodically checks for new images.
            setInterval(checkForNewImages, 5000); // every 5 seconds

            window.addEventListener('resize', debounce(handleResize, 500));
        });
        <?php } else { ?>
        document.addEventListener('DOMContentLoaded', () => {
            createLightbox(false);
            setupLightboxScroll();
            showOneImage(<?php echo $imageId; ?>);
        });
        <?php } ?>

        /**
         * The 5-second timer calls this; if near top, we load new images.
         */
        function checkForNewImages() {
            if (window.scrollY < 100 && highestImageId > 0 && !isLoading) {
                console.log("Timer: at top, checking for new images...");
                const needed = calculateRequiredImages();
                if (needed > 0) fetchAndRenderQueue('new', needed);
            }
        }

        function createLightbox(closeable = true) {
            const lightbox = document.getElementById('lightbox');
            lightbox.innerHTML = '';
            if (closeable) {
                const closeButton = document.createElement('div');
                closeButton.classList.add('close-btn');
                closeButton.innerHTML = '&times;';
                closeButton.onclick = (e) => {
                    e.stopPropagation();
                    lightbox.style.display = 'none';
                    document.body.style.overflow = '';
                };
                lightbox.appendChild(closeButton);

                lightbox.onclick = () => {
                    lightbox.style.display = 'none';
                    document.body.style.overflow = '';
                };
            }
        }

        function setupLightboxScroll() {
            const lightbox = document.getElementById('lightbox');
            lightbox.addEventListener('wheel', (e) => {
                e.preventDefault();
                if (e.deltaY < 0) showNextImage();
                else if (e.deltaY > 0) showPreviousImage();
            });
        }

        function fetchImageUrl(imageId) {
            return `/api/get-image.php?id=${imageId}`;
        }

        function shortenText(text, maxLength) {
            const isTextShortened = text.length > maxLength;
            const shortenedText = isTextShortened ? text.substring(0, maxLength - 3) + "..." : text;
            return { shortenedText, isTextShortened };
        }

        function generateCaption(image, shortenChars = 100) {
            const { shortenedText: promptShort, isTextShortened: pFlag } = shortenText(image.Prompt, shortenChars);
            const { shortenedText: negShort, isTextShortened: nFlag } = shortenText(image.NegativePrompt, shortenChars);

            const { DateTime } = luxon;
            const serverTime = DateTime.fromISO(image.DateCreated, { zone: 'America/Chicago' });
            const dateAdded = serverTime.setZone(DateTime.local().zoneName);

            let caption =
                `<div><strong>ID:</strong> <a href="?id=${image.ID}">${image.ID}</a><br></div>
                <?php if ($user['access_level'] == 'ADMIN'): ?>
                <div><strong>User:</strong> <a href="?uid=${image.UID}">${image.Username ? image.Username : '(' + (image.Firstname || '') + (image.Firstname && image.Lastname ? ' ' : '') + (image.Lastname || '') + ')'}</a><br></div>
                <?php endif; ?>
                <div>${image.TeleChatID == image.TeleID ? "" : '<strong>Chat:</strong> ' + image.TeleChatID + '<br>'}</div>
                ${image.Prompt ? `<div><strong>Prompt:</strong> <span class="${pFlag ? 'shorten' : ''}" ${pFlag ? `data-fulltext="${escapeHTML(image.Prompt)}" data-shorttext="${escapeHTML(promptShort)}"` : ''}>${escapeHTML(promptShort)}</span><br></div>` : ''}
                ${image.NegativePrompt ? `<div><strong>Negative:</strong> <span class="${nFlag ? 'shorten' : ''}" ${nFlag ? `data-fulltext="${escapeHTML(image.NegativePrompt)}" data-shorttext="${escapeHTML(negShort)}"` : ''}>${escapeHTML(negShort)}</span><br></div>` : ''}
                ${image.HiresEnabled ? `<div><strong>Size:</strong> ${image.HiresWidth}x${image.HiresHeight} (from ${image.Width}x${image.Height}) <br></div>` : `<div><strong>Size:</strong> ${image.Width}x${image.Height}<br></div>`}
                <div><strong>Sampler Steps:</strong> ${image.Steps}<br></div>
                <div><strong>CFG Scale:</strong> ${image.CFGScale}<br></div>
                ${image.Type === 'IMG2IMG' ? `<div><strong>Denoising Strength:</strong> ${image.DenoisingStrength}<br></div>` : ''}
                <div><strong>Model:</strong> <a href="?uid=<?php echo $uid; ?>&model=${encodeURIComponent(image.Model)}">${escapeHTML(image.Model)}</a><br></div>
                <div><strong>Seed:</strong> ${image.Seed}<br></div>
                <div><strong>Sampler:</strong> ${escapeHTML(image.Sampler)}<br></div>
                <?php if ($user['access_level'] == 'ADMIN'): ?>
                <div><strong>Worker:</strong> ${escapeHTML(image.WorkerName)}</div>
                <?php endif; ?>
                <div>${escapeHTML(dateAdded.toFormat('dd LLL yyyy hh:mm:ss a ZZZZ'))}</div>`;

            return caption;
        }

        function escapeHTML(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }

        function setupShortenForElement(element) {
            element.querySelectorAll('.shorten').forEach(span => {
                if (span.dataset.shorttext && span.dataset.fulltext && span.dataset.shorttext !== span.dataset.fulltext) {
                    span.classList.add('can-expand');
                    span.onclick = function() {
                        if (this.classList.contains('active')) {
                            this.classList.remove('active');
                            this.textContent = this.getAttribute('data-shorttext');
                        } else {
                            this.classList.add('active');
                            this.textContent = this.getAttribute('data-fulltext');
                        }
                    };
                }
            });
        }

        function displayImages(images, action) {
            console.log(`Displaying images with action: ${action}`);
            const container = document.getElementById('imageContainer');
            let newImagesCount = 0;

            images.forEach(image => {
                const idx = image.ID;
                if (imagesData[idx]) {
                    console.log(`Image ID ${idx} already exists. Skipping.`);
                    return;
                }
                imagesData[idx] = image;
                totalImagesLoaded++;
                newImagesCount++;
                console.log(`Displaying Image ID: ${idx}`);

                const wrapper = document.createElement('div');
                wrapper.classList.add('image-wrapper');

                const primaryImg = document.createElement('img');
                primaryImg.src = fetchImageUrl(image.ImageID);
                primaryImg.loading = 'lazy';
                primaryImg.setAttribute('data-image-id', idx);

                primaryImg.onload = function() {
                    if (!wrapper.dataset.storedHeight) {
                        const wrapperHeight = wrapper.offsetHeight;
                        wrapper.style.height = wrapperHeight + 'px';
                        wrapper.dataset.storedHeight = wrapperHeight;
                        console.log(`Stored wrapper height for ID: ${idx} => ${wrapperHeight}px`);
                    }
                };

                primaryImg.onclick = () => handleImageClick(idx);
                wrapper.appendChild(primaryImg);
                console.log(`Added primary image: ${image.ImageID}`);

                if (image.Type === "IMG2IMG" && image.SelectedImage) {
                    const secondaryImg = document.createElement('img');
                    secondaryImg.src = fetchImageUrl(image.SelectedImage);
                    secondaryImg.loading = 'lazy';
                    secondaryImg.setAttribute('data-image-id', idx);

                    secondaryImg.onload = function() {
                        if (!wrapper.dataset.storedHeight) {
                            const wrapperHeight = wrapper.offsetHeight;
                            wrapper.style.height = wrapperHeight + 'px';
                            wrapper.dataset.storedHeight = wrapperHeight;
                            console.log(`Stored wrapper height (secondary) for ID: ${idx} => ${wrapperHeight}px`);
                        }
                    };

                    secondaryImg.onclick = () => handleImageClick(idx);
                    wrapper.appendChild(secondaryImg);
                    console.log(`Added secondary image for IMG2IMG type: ${image.SelectedImage}`);
                }

                const textDiv = document.createElement('div');
                textDiv.className = 'caption';
                textDiv.innerHTML = generateCaption(image, 400);
                setupShortenForElement(textDiv);
                wrapper.appendChild(textDiv);

                if (action === 'new') {
                    container.insertBefore(wrapper, container.firstChild);
                    console.log(`Prepended Image ID: ${idx}`);
                } else {
                    container.appendChild(wrapper);
                    console.log(`Appended Image ID: ${idx}`);
                }
            });

            updateImageIds(images, action);
            return newImagesCount;
        }

        function updateImageIds(images, action) {
            if (action === 'new') {
                const maxID = Math.max(...images.map(img => img.ID));
                if (maxID > highestImageId) {
                    highestImageId = maxID;
                    console.log(`Updated highestImageId to: ${highestImageId}`);
                }
            } else {
                const minID = Math.min(...images.map(img => img.ID));
                lastImageId = minID - 1;
                console.log(`Updated lastImageId to: ${lastImageId}`);
            }
        }

        function handleImageClick(idx) {
            const image = imagesData[idx];
            if (!image) return;
            showLightbox(idx);
        }

        function showLightbox(idx) {
            const lightbox = document.getElementById('lightbox');
            lightbox.innerHTML = '';
            createLightbox();

            currentImageIdx = idx;
            const image = imagesData[idx];
            if (!image) return;

            const captionHTML = generateCaption(image, 400);

            const container = document.createElement('div');
            container.classList.add('image-container');

            const img = document.createElement('img');
            img.src = fetchImageUrl(image.ImageID) + '&full=1';
            img.loading = 'lazy';
            container.appendChild(img);

            const captionDiv = document.createElement('div');
            captionDiv.innerHTML = captionHTML;
            captionDiv.classList.add('caption');
            captionDiv.onclick = e => e.stopPropagation();
            setupShortenForElement(captionDiv);

            img.onload = function() {
                if (this.naturalWidth < window.innerWidth && this.naturalHeight < window.innerHeight) {
                    this.style.width = Math.min(this.naturalWidth, window.innerWidth * 0.9) + 'px';
                    this.style.height = 'auto';
                }
                const totalHeight = this.offsetHeight + captionDiv.offsetHeight;
                if (window.innerHeight > totalHeight) {
                    container.appendChild(captionDiv);
                }
            };

            container.appendChild(captionDiv);
            lightbox.appendChild(container);
            lightbox.style.display = 'flex';
            document.body.style.overflow = 'hidden';
        }

        function showPreviousImage() {
            const keys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
            const curIndex = keys.indexOf(Number(currentImageIdx));
            if (curIndex > 0) {
                const prevIndex = keys[curIndex - 1];
                if (imagesData[prevIndex]) showLightbox(prevIndex.toString());
            } else {
                const req = calculateRequiredImages();
                if (req > 0) {
                    fetchAndRenderQueue('old', req).then(() => {
                        const updated = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
                        const newIndex = updated.indexOf(Number(currentImageIdx));
                        if (newIndex > 0) showPreviousImage();
                    });
                }
            }
        }

        function showNextImage() {
            const keys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
            const curIndex = keys.indexOf(Number(currentImageIdx));
            if (curIndex < keys.length - 1) {
                const nextIndex = keys[curIndex + 1];
                if (imagesData[nextIndex]) showLightbox(nextIndex.toString());
            } else {
                const req = calculateRequiredImages();
                if (req > 0) {
                    fetchAndRenderQueue('new', req).then(() => {
                        const updated = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
                        const newIndex = updated.indexOf(Number(currentImageIdx));
                        if (newIndex < updated.length - 1) showNextImage();
                    });
                }
            }
        }

        function showOneImage(idx) {
            return ListQueue({
                ID: parseInt(idx, 10),
                PageSize: 1
            }).then(queueItems => {
                if (queueItems.length > 0) {
                    const image = queueItems[0];
                    imagesData[image.ID] = image;
                    totalImagesLoaded++;
                    displayImages([image], 'new');
                    showLightbox(image.ID);
                    return image;
                } else {
                    throw 'No images returned';
                }
            }).catch(err => console.error(err));
        }

        function fetchAndRenderQueue(action = 'old', pageSize = 20) {
            if (isLoading) {
                console.log("Already loading. Skipping fetch.");
                return Promise.resolve();
            }
            isLoading = true;
            document.getElementById('loading').style.display = 'block';
            document.getElementById('error-message').style.display = 'none';

            let modelFilter = '<?php echo $imageModel; ?>';
            let statusFilter = 'FINISHED';

            let requestParams = {
                UID: <?php echo $uid; ?>,
                action,
                PageSize: pageSize,
                Status: statusFilter,
                Model: modelFilter
            };
            if (action === 'old' && lastImageId > 0) {
                requestParams.lastImageId = lastImageId;
            } else if (action === 'new' && highestImageId > 0) {
                requestParams.lastImageId = highestImageId;
            }

            return ListQueue(requestParams).then(queueItems => {
                if (queueItems.length > 0) {
                    displayImages(queueItems, action);
                } else {
                    if (action === 'old') hasMoreOldImages = false;
                }
                document.getElementById('loading').style.display = 'none';
                isLoading = false;

                checkAndLoadMoreImages();
                unloadDistantImages();
            }).catch(err => {
                document.getElementById('loading').style.display = 'none';
                document.getElementById('error-message').textContent = 'Failed to load queue data.';
                document.getElementById('error-message').style.display = 'block';
                isLoading = false;
            });
        }

        function setupInfiniteScroll() {
            window.addEventListener('scroll', debounce(handleScroll, 200));
        }

        function handleScroll() {
            const scrollTop = window.scrollY;
            const windowHeight = window.innerHeight;
            const docHeight = document.body.offsetHeight;

            const thresholdBottom = windowHeight;
            if ((scrollTop + windowHeight) >= (docHeight - thresholdBottom) && !isLoading && hasMoreOldImages) {
                console.log("Less-bulk near bottom => fetching old images...");
                const needed = calculateRequiredImages();
                if (needed > 0) fetchAndRenderQueue('old', needed);
            }

            const thresholdTop = windowHeight;
            if (scrollTop <= thresholdTop && !isLoading) {
                console.log("Less-bulk near top => fetching new images...");
                const needed = calculateRequiredImages();
                if (needed > 0) fetchAndRenderQueue('new', needed);
            }

            unloadDistantImages();
        }

        function debounce(func, wait) {
            let timeout;
            return function(...args) {
                clearTimeout(timeout);
                timeout = setTimeout(() => func.apply(this, args), wait);
            };
        }
    </script>
</body>
</html>
