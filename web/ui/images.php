<?php

//-----------------------------------------

require_once("../../lib/web/lib_default.php");
require_once("../../lib/web/lib_login.php");

if (!checkUserLogin())
	exit;

if ($user['access_level'] == 'ADMIN' && isset($_GET['uid']) && is_numeric($_GET['uid'])) {
	$uid = (int)$_GET['uid'];
	if ($uid == -2)
		$uid = $user['id'];
} else
	$uid = -1;


$dark = (isset($_GET['dark']) && $_GET['dark']);

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

</head>
<body>
    <div id="imageContainer"></div>
    <div id="loading">Loading more images...</div>
    <script>
        let lastImageId = 0;
        let highestImageId = 0;
        let isLoading = false;

		let imagesData = {};

		let currentImageIdx = null; // Global variable to track the current image index

		document.addEventListener('DOMContentLoaded', () => {
			createLightbox(); // Ensure this is called once the DOM is fully loaded
			setupLightboxScroll(); // Setup scroll functionality in lightbox
		});

        function createLightbox() {
            const lightbox = document.createElement('div');
            lightbox.id = 'lightbox';
            document.body.appendChild(lightbox);

			const closeButton = document.createElement('div');
			closeButton.classList.add('close-btn');
			closeButton.innerHTML = '&times;';
			lightbox.appendChild(closeButton);

            closeButton.addEventListener('click', (e) => {
                e.stopPropagation();
                lightbox.style.display = 'none';
                document.body.style.overflow = '';
            });

            lightbox.addEventListener('click', () => {
                lightbox.style.display = 'none';
                document.body.style.overflow = '';
            });
        }

		function showLightbox(imageUrl, idx) {
			const lightbox = document.getElementById('lightbox');
			lightbox.innerHTML = lightbox.querySelector('.close-btn').outerHTML;

			currentImageIdx = idx; // Update the global variable with the current index

			const image = imagesData[idx];
			if (!image) {
				console.error("Image data not found for idx:", idx);
				return;
			}

			const captionHTML = constructCaptionHTML(image);

			const imageContainer = document.createElement('div');
			imageContainer.classList.add('image-container'); // Use class for styling

			const img = document.createElement('img');
			img.src = imageUrl;
			img.alt = "Full size image";
			imageContainer.appendChild(img);

			const captionDiv = document.createElement('div');
			captionDiv.innerHTML = captionHTML;
			captionDiv.classList.add('caption'); // Use class for styling
			captionDiv.addEventListener('click', (e) => e.stopPropagation());

			img.onload = function() {
				if (this.naturalWidth < window.innerWidth && this.naturalHeight < window.innerHeight) {
					this.style.width = Math.min(this.naturalWidth, window.innerWidth * 0.9) + 'px';
					this.style.height = 'auto';
				}
				let totalHeight = this.offsetHeight + captionDiv.offsetHeight;
				if (window.innerHeight > totalHeight) {
					imageContainer.appendChild(captionDiv);
				}
			};

			lightbox.appendChild(imageContainer);
			lightbox.style.display = 'flex';
			document.body.style.overflow = 'hidden';
		}

		function setupLightboxScroll() {
			const lightbox = document.getElementById('lightbox');

			lightbox.addEventListener('wheel', (e) => {
				e.preventDefault(); // Prevent the page from scrolling

				if (e.deltaY < 0) {
					// Scrolling up, show previous image
					showNextImage();
				} else if (e.deltaY > 0) {
					// Scrolling down, show next image
					showPreviousImage();
				}
			});
		}

function showPreviousImage() {
    const keys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
    const currentIndex = keys.indexOf(Number(currentImageIdx));
    if (currentIndex > 0) {
        const previousIndex = keys[currentIndex - 1];
        const previousImage = imagesData[previousIndex];
        if (previousImage) {
            const fullResolutionUrl = `${previousImage.imageUrl}&full=1`;
            showLightbox(fullResolutionUrl, previousIndex.toString());
        }
    } else {
        fetchImages('old').then(() => {
            // Re-fetch keys as they might have been updated
            const updatedKeys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
            const newCurrentIndex = updatedKeys.indexOf(Number(currentImageIdx));
            if (newCurrentIndex > 0) {
                showPreviousImage(); // Try showing the previous image again
            }
        }).catch(error => console.error(error));
    }
}

function showNextImage() {
    const keys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
    const currentIndex = keys.indexOf(Number(currentImageIdx));
    if (currentIndex < keys.length - 1) {
        const nextIndex = keys[currentIndex + 1];
        const nextImage = imagesData[nextIndex];
        if (nextImage) {
            const fullResolutionUrl = `${nextImage.imageUrl}&full=1`;
            showLightbox(fullResolutionUrl, nextIndex.toString());
        }
    } else {
        fetchImages('new').then(() => {
            const updatedKeys = Object.keys(imagesData).map(Number).sort((a, b) => a - b);
            const newCurrentIndex = updatedKeys.indexOf(Number(currentImageIdx));
            if (newCurrentIndex < updatedKeys.length - 1) {
                showNextImage(); // Try showing the next image again
            }
        }).catch(error => console.error(error));
    }
}




		function constructCaptionHTML(image) {
			let caption = '';

			if (image.prompt) {
				caption += `<div><strong>Prompt: </strong>${image.prompt}</div>`;
			}
			if (image.negative_prompt) {
				caption += `<br><div><strong>Negative Prompt: </strong>${image.negative_prompt}</div>`;
			}
			// Add more fields from the image object as needed
			return caption;
		}

		function handleImageClick(idx) {
			const image = imagesData[idx];
			if (!image) {
				console.error("Image data not found for idx:", idx);
				return;
			}
			const fullResolutionUrl = `${image.imageUrl}&full=1`;
			showLightbox(fullResolutionUrl, idx); // Pass idx as well
		}

        function updateImageIds(images, action) {
			Object.values(images).forEach(image => { // Use Object.values() to get the array of values
				if (action === 'new') {
					highestImageId = Math.max(highestImageId, parseInt(image.id, 10)); // Ensure id is treated as a number
					
					if (lastImageId === 0 || parseInt(image.id, 10) < lastImageId) {
						lastImageId = parseInt(image.id, 10);
					}
				} else {
					if (lastImageId === 0 || parseInt(image.id, 10) < lastImageId) {
						lastImageId = parseInt(image.id, 10);
					}
				}
			});
		}

		function shortenText(text, maxLength) {
			const isTextShortened = text.length > maxLength;
			const shortenedText = isTextShortened ? text.substring(0, maxLength - 3) + "..." : text;
			return { shortenedText, isTextShortened };
		}

		function displayImages(images, action) {
			const container = document.getElementById('imageContainer');
			Object.entries(images).forEach(([idx, image]) => {
				const imgElement = document.createElement('img');
				imgElement.src = image.imageUrl; // Updated to use imageUrl
				imgElement.setAttribute('data-image-id', idx); // Set the image ID as a data attribute
				imgElement.addEventListener('click', () => handleImageClick(idx)); // Add click listener
				imgElement.style.width = '100%'; // Ensure the image takes the full width of its container
				imgElement.style.height = 'auto'; // Maintain aspect ratio

				// Create a text element to display under the image
				
				let q = image;

				let { shortenedText: promptShortened, isTextShortened: promptShortenedFlag } = shortenText(q.prompt, 100);
				let { shortenedText: negativeShortened, isTextShortened: negativeShortenedFlag } = shortenText(q.negative_prompt, 100);

				// Assuming q.date_added is in Central Time (America/Chicago)
				// Luxon is used to parse and convert the timestamp
				const { DateTime } = luxon;
				// Parse the timestamp with milliseconds from Central Time and convert to the user's local timezone
				const serverTime = DateTime.fromFormat(q.date_added, "yyyy-MM-dd HH:mm:ss.SSS", { zone: 'America/Chicago' });
				const dateAdded = serverTime.setZone(DateTime.local().zoneName);

				let caption = 
<?php if ($user['access_level'] == 'ADMIN'): ?>
				'<div><strong>User:</strong> <a href="?uid=' + q.uid + '">' + (q.username ? q.username : '(' + (q.firstname || '') + (q.firstname && q.lastname ? ' ' : '') + (q.lastname || '') + ')') + '</a><br></div>' +
<?php endif; ?>
				'<div>' + (q.tele_chatid == q.tele_id ? "" : '<strong>Chat:</strong> ' + q.tele_chatid + '<br>') + '</div>' +
				(q.prompt ? '<div><strong>Prompt:</strong> <span class="' + (promptShortenedFlag ? 'shorten can-expand' : 'shorten') + '" data-fulltext="' + q.prompt + '" data-shorttext="' + promptShortened + '">' + promptShortened + '</span><br></div>' : '') +
				(q.negative_prompt ? '<div><strong>Negative:</strong> <span class="' + (negativeShortenedFlag ? 'shorten can-expand' : 'shorten') + '" data-fulltext="' + q.negative_prompt + '" data-shorttext="' + negativeShortened + '">' + negativeShortened + '</span><br></div>' : '') +
				'<div><strong>Size:</strong> ' + q.width + 'x' + q.height + '<br></div>' +
				'<div><strong>Sampler Steps:</strong> ' + q.steps + '<br></div>' +
				'<div><strong>CFG Scale:</strong> ' + q.cfgscale + '<br></div>' +
				'<div><strong>Denoising Strength:</strong> ' + q.denoising_strength + '<br></div>' +
				'<div><strong>Seed:</strong> ' + q.seed + '<br></div>' +
				'<div>' + dateAdded.toFormat('dd LLL yyyy hh:mm:ss a ZZZZ') + '</div>'; // Appending the formatted timestamp with timezone

				const textElement = document.createElement('div');
				textElement.innerHTML = caption; // Use innerHTML to insert formatted HTML
				textElement.className = 'caption'; // Assign the 'caption' class for styling

				setupShortenForElement(textElement);

				const wrapper = document.createElement('div');
				wrapper.classList.add('image-wrapper');
				wrapper.appendChild(imgElement);
				wrapper.appendChild(textElement); // Add the text element under the image

				if (action === 'new') {
					container.insertBefore(wrapper, container.firstChild);
				} else {
					container.appendChild(wrapper);
				}
			});
		}

		let hasMoreOldImages = true;

		function fetchImages(action = 'old') {
			return new Promise((resolve, reject) => {
				if (isLoading) return reject('Already loading images.');
				isLoading = true;
				let queryParam = `lastImageId=${action === 'new' ? highestImageId : lastImageId}`;
				fetch(`/api/list-images.php?action=${action}&${queryParam}&uid=<?php echo $uid; ?>`)
					.then(response => response.json())
					.then(data => {
						if (data && data.images) {
							const newImages = data.images;
							Object.entries(newImages).forEach(([key, value]) => {
								imagesData[key] = value; // Update or add the new images to imagesData
							});
							updateImageIds(newImages, action); // Make sure to implement or adjust this function as needed
							displayImages(newImages, action); // Adjust based on your implementation
							isLoading = false;
							resolve(newImages);
						} else {
							isLoading = false;
							reject('No images returned');
						}
					}).catch(error => {
						console.error('Fetch error:', error);
						isLoading = false;
						reject(error);
					});
			});
}

		// Define a function to check for new images
		function checkForNewImages() {
			// Only fetch new images if at the top of the page and not currently loading other images
			if ((window.scrollY < 100 && !isLoading && highestImageId > 0) || (imagesData === null || Object.keys(imagesData).length === 0)) {
				console.log("At top, checking for new images...");
				fetchImages('new');
			}
		}

		function setupShortenForElement(element) {
			element.querySelectorAll('.shorten').forEach(span => {
				span.onclick = function() {
					if (this.classList.contains('active')) {
						this.classList.remove('active');
						this.textContent = this.getAttribute('data-shorttext');
					} else {
						this.classList.add('active');
						this.textContent = this.getAttribute('data-fulltext');
					}
				};
			});
		}

		// Set up an interval to periodically check for new images
		const checkInterval = 1000; // Check every 5000 milliseconds (5 seconds)
		setInterval(checkForNewImages, checkInterval);

		window.addEventListener('scroll', () => {
			// Check if scrolling near the top to fetch new images
			if (window.scrollY < 100 && !isLoading && highestImageId > 0) {
				console.log("Near top, fetching newer images...");
				fetchImages('new');
			}
			// Determine the distance from the bottom of the document or container
			const scrollPosition = window.innerHeight + window.scrollY;
			const threshold = document.body.offsetHeight - 800; // Fetch when 800px away from the bottom
			// Check if scrolling near the bottom to fetch older images
			if (scrollPosition >= threshold && !isLoading && hasMoreOldImages) {
				console.log("Near bottom, fetching more images...");
				fetchImages('old');
			}
		});


        fetchImages('new'); // Attempt to load new images initially
    </script>
</body>
</html>
