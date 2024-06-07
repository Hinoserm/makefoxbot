/*
 Navicat Premium Data Transfer

 Source Server         : Dextrose
 Source Server Type    : MariaDB
 Source Server Version : 100523 (10.5.23-MariaDB)
 Source Host           : localhost:3306
 Source Schema         : makefoxbot

 Target Server Type    : MariaDB
 Target Server Version : 100523 (10.5.23-MariaDB)
 File Encoding         : 65001

 Date: 05/06/2024 17:12:30
*/

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for admin_chats
-- ----------------------------
DROP TABLE IF EXISTS `admin_chats`;
CREATE TABLE `admin_chats`  (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `from_uid` bigint(20) UNSIGNED NOT NULL COMMENT 'Admin\'s UID',
  `to_uid` bigint(20) UNSIGNED NOT NULL COMMENT 'Receiving user\'s telegram ID',
  `tg_peer_id` bigint(20) NULL DEFAULT NULL COMMENT 'Receiving Telegram peer ID (chat or user)',
  `message` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `message_date` datetime NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 125 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for admin_open_chats
-- ----------------------------
DROP TABLE IF EXISTS `admin_open_chats`;
CREATE TABLE `admin_open_chats`  (
  `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT,
  `from_uid` bigint(20) UNSIGNED NOT NULL COMMENT 'The admin\'s UID.',
  `to_uid` bigint(20) UNSIGNED NOT NULL COMMENT 'Destination UID.',
  `tg_peer_id` bigint(20) NULL DEFAULT NULL COMMENT 'Telegram Peer ID (Chat or User)',
  `date_opened` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 110 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for gpu_stats
-- ----------------------------
DROP TABLE IF EXISTS `gpu_stats`;
CREATE TABLE `gpu_stats`  (
  `when` datetime(2) NOT NULL,
  `uuid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `model_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `serial_number` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `temperature` int(10) UNSIGNED NOT NULL COMMENT 'In whole degrees Celsius',
  `gpu_utilization` int(10) UNSIGNED NOT NULL COMMENT 'In percent',
  `mem_utilization` int(10) UNSIGNED NOT NULL COMMENT 'In percent',
  `power_usage` int(10) UNSIGNED NOT NULL COMMENT 'In milliwatts',
  `fan_speed` int(10) UNSIGNED NOT NULL COMMENT 'In percent',
  `sm_clock` int(10) UNSIGNED NOT NULL COMMENT 'In MHz',
  `gpu_clock` int(11) UNSIGNED NOT NULL COMMENT 'In MHz',
  `mem_clock` int(11) UNSIGNED NOT NULL COMMENT 'In MHz',
  `power_state` int(10) UNSIGNED NOT NULL COMMENT 'Lower is faster, starting at 0',
  `mem_total` bigint(20) UNSIGNED NOT NULL,
  `mem_used` bigint(20) UNSIGNED NOT NULL,
  `mem_free` bigint(20) UNSIGNED NOT NULL,
  `pcie_rx` int(11) UNSIGNED NOT NULL COMMENT 'In MB/sec',
  `pcie_tx` int(11) UNSIGNED NOT NULL COMMENT 'In MB/sec',
  `driver_version` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  PRIMARY KEY (`when`, `uuid`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for images
-- ----------------------------
DROP TABLE IF EXISTS `images`;
CREATE TABLE `images`  (
  `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT,
  `type` enum('INPUT','OUTPUT','OTHER','UNKNOWN') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'UNKNOWN',
  `user_id` bigint(20) UNSIGNED NOT NULL,
  `filename` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `filesize` bigint(20) UNSIGNED NULL DEFAULT NULL,
  `image` longblob NULL DEFAULT NULL,
  `image_file` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'Where the image file is stored on the server',
  `sha1hash` varchar(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `date_added` datetime NOT NULL,
  `telegram_fileid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'The Telegram file ID for the smaller, compressed image, once it\'s been uploaded.',
  `telegram_uniqueid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `telegram_full_fileid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'The Telegram file ID for the full-size uncompressed image, once it\'s been uploaded.',
  `telegram_full_uniqueid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `telegram_chatid` bigint(20) NULL DEFAULT NULL,
  `telegram_msgid` bigint(20) NULL DEFAULT NULL,
  `hidden` tinyint(1) NOT NULL DEFAULT 0,
  `flagged` tinyint(1) NULL DEFAULT NULL COMMENT 'Flagged for review',
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `idx3`(`user_id`, `telegram_uniqueid`, `date_added`) USING BTREE,
  INDEX `idx1`(`user_id`) USING BTREE,
  INDEX `idx2`(`user_id`, `telegram_chatid`, `date_added`) USING BTREE,
  INDEX `idx4`(`type`) USING BTREE,
  INDEX `idx5`(`type`, `id`) USING BTREE,
  INDEX `idx6`(`flagged`) USING BTREE,
  INDEX `idx7`(`telegram_msgid`) USING BTREE,
  CONSTRAINT `images_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE = InnoDB AUTO_INCREMENT = 2385851 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for queue
-- ----------------------------
DROP TABLE IF EXISTS `queue`;
CREATE TABLE `queue`  (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `status` enum('CANCELLED','PENDING','PROCESSING','PROCESSED','SENDING','FINISHED','ERROR') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'FINISHED',
  `type` enum('UNKNOWN','TXT2IMG','IMG2IMG') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `uid` bigint(20) NOT NULL,
  `tele_id` bigint(20) NOT NULL,
  `tele_chatid` bigint(20) NULL DEFAULT NULL,
  `image_id` bigint(20) UNSIGNED NULL DEFAULT NULL,
  `model` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `steps` int(11) NULL DEFAULT NULL,
  `seed` int(11) NULL DEFAULT NULL,
  `cfgscale` decimal(6, 2) NULL DEFAULT NULL,
  `prompt` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `negative_prompt` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `width` int(11) NULL DEFAULT NULL,
  `height` int(11) NULL DEFAULT NULL,
  `denoising_strength` decimal(6, 2) NULL DEFAULT NULL,
  `selected_image` bigint(20) UNSIGNED NULL DEFAULT NULL,
  `enhanced` tinyint(1) UNSIGNED NOT NULL DEFAULT 0,
  `reply_msg` bigint(20) NULL DEFAULT NULL,
  `msg_id` bigint(20) NULL DEFAULT NULL,
  `msg_deleted` bigint(1) NOT NULL DEFAULT 0,
  `retry_count` int(11) NOT NULL DEFAULT 0,
  `error_str` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `retry_date` datetime NULL DEFAULT NULL,
  `sent` tinyint(1) NOT NULL DEFAULT 0,
  `date_added` datetime(3) NOT NULL,
  `date_worker_start` datetime(3) NULL DEFAULT NULL,
  `date_sent` datetime(3) NULL DEFAULT NULL,
  `date_finished` datetime(3) NULL DEFAULT NULL,
  `date_failed` datetime(3) NULL DEFAULT NULL,
  `link_token` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `worker` int(11) NULL DEFAULT NULL,
  `original_id` bigint(20) NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `idx1`(`status`) USING BTREE,
  INDEX `idx2`(`date_added`) USING BTREE,
  INDEX `idx3`(`date_failed`) USING BTREE,
  INDEX `idx4`(`status`, `date_failed`) USING BTREE,
  INDEX `idx5`(`link_token`) USING BTREE,
  INDEX `idx6`(`uid`) USING BTREE,
  INDEX `idx7`(`status`, `uid`) USING BTREE,
  INDEX `idx8`(`tele_id`, `tele_chatid`) USING BTREE,
  INDEX `idx9`(`status`, `date_added`) USING BTREE,
  INDEX `idx10`(`selected_image`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 2278074 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for send_queue
-- ----------------------------
DROP TABLE IF EXISTS `send_queue`;
CREATE TABLE `send_queue`  (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `queue_id` bigint(20) NOT NULL COMMENT 'Links back to an item in the \'queue\' table.',
  `status` enum('PENDING','ERROR','SENT') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'SENT',
  `tuser_id` bigint(20) NOT NULL,
  `tchat_id` bigint(20) NOT NULL,
  `date_added` datetime NOT NULL COMMENT 'Date the item was added to the queue',
  `date_sent` datetime NULL DEFAULT NULL COMMENT 'Date the item was sent, null if not yet sent.',
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `idx1`(`queue_id`) USING BTREE,
  INDEX `idx2`(`status`, `date_added`) USING BTREE,
  INDEX `idx3`(`tchat_id`, `status`, `date_sent`) USING BTREE,
  CONSTRAINT `send_queue_ibfk_1` FOREIGN KEY (`queue_id`) REFERENCES `queue` (`id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for sessions
-- ----------------------------
DROP TABLE IF EXISTS `sessions`;
CREATE TABLE `sessions`  (
  `session_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `uid` bigint(20) UNSIGNED NOT NULL,
  `session_data` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `date_added` datetime NOT NULL,
  `date_deleted` datetime NULL DEFAULT NULL,
  PRIMARY KEY (`session_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for settings
-- ----------------------------
DROP TABLE IF EXISTS `settings`;
CREATE TABLE `settings`  (
  `key` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `value` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  PRIMARY KEY (`key`) USING BTREE,
  INDEX `idx`(`key`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_chat_admins
-- ----------------------------
DROP TABLE IF EXISTS `telegram_chat_admins`;
CREATE TABLE `telegram_chat_admins`  (
  `chatid` bigint(20) NOT NULL,
  `userid` bigint(20) NOT NULL,
  `type` enum('UNKNOWN','ADMIN','CREATOR') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT 'ADMIN',
  `rank` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `flags` int(11) NULL DEFAULT NULL,
  `date_updated` datetime NULL DEFAULT NULL,
  PRIMARY KEY (`userid`, `chatid`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_chats
-- ----------------------------
DROP TABLE IF EXISTS `telegram_chats`;
CREATE TABLE `telegram_chats`  (
  `id` bigint(20) NOT NULL,
  `access_hash` bigint(20) NULL DEFAULT NULL,
  `active` tinyint(1) NULL DEFAULT NULL,
  `type` enum('PRIVATE','GROUP','SUPERGROUP','GIGAGROUP','CHANNEL','UNKNOWN') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'UNKNOWN',
  `username` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `level` int(11) NULL DEFAULT NULL,
  `flags` bigint(20) NULL DEFAULT NULL,
  `flags2` bigint(20) NULL DEFAULT NULL,
  `admin_flags` bigint(20) NULL DEFAULT NULL,
  `participants` int(11) NULL DEFAULT NULL,
  `photo_id` bigint(20) NULL DEFAULT NULL,
  `photo` longblob NULL DEFAULT NULL,
  `date_added` datetime NULL DEFAULT NULL,
  `date_updated` datetime NOT NULL,
  `last_full_update` datetime NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_log
-- ----------------------------
DROP TABLE IF EXISTS `telegram_log`;
CREATE TABLE `telegram_log`  (
  `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id` bigint(20) NOT NULL,
  `chat_id` bigint(20) NULL DEFAULT NULL,
  `message_id` int(11) NULL DEFAULT NULL,
  `message_text` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `message_deleted` tinyint(1) NOT NULL DEFAULT 0,
  `date_added` datetime NOT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `idx1`(`user_id`, `chat_id`, `date_added`) USING BTREE,
  INDEX `idx2`(`message_id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 2772371 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_user_settings
-- ----------------------------
DROP TABLE IF EXISTS `telegram_user_settings`;
CREATE TABLE `telegram_user_settings`  (
  `uid` bigint(20) NOT NULL,
  `tele_id` bigint(20) NOT NULL,
  `tele_chatid` bigint(20) NOT NULL,
  `model` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `steps` int(11) NULL DEFAULT NULL,
  `seed` int(11) NULL DEFAULT NULL,
  `cfgscale` decimal(6, 2) NULL DEFAULT NULL,
  `prompt` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `negative_prompt` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `width` int(11) NULL DEFAULT NULL,
  `height` int(11) NULL DEFAULT NULL,
  `denoising_strength` decimal(6, 2) NULL DEFAULT NULL,
  `selected_image` bigint(20) NULL DEFAULT NULL,
  PRIMARY KEY (`tele_id`, `tele_chatid`, `uid`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_users
-- ----------------------------
DROP TABLE IF EXISTS `telegram_users`;
CREATE TABLE `telegram_users`  (
  `id` bigint(20) NOT NULL,
  `access_hash` bigint(20) NOT NULL,
  `type` enum('USER','BOT') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'USER',
  `active` tinyint(1) NULL DEFAULT NULL,
  `language` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
  `username` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `firstname` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `lastname` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `bio` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
  `flags` int(11) NULL DEFAULT NULL,
  `flags2` int(11) NULL DEFAULT NULL,
  `photo_id` bigint(20) NULL DEFAULT NULL,
  `photo` longblob NULL DEFAULT NULL,
  `date_added` datetime NULL DEFAULT NULL,
  `date_updated` datetime NOT NULL,
  `last_full_update` datetime NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for user_payments
-- ----------------------------
DROP TABLE IF EXISTS `user_payments`;
CREATE TABLE `user_payments`  (
  `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Payment ID.',
  `uid` bigint(20) UNSIGNED NOT NULL COMMENT 'User\'s ID.',
  `date` datetime NOT NULL COMMENT 'Date of payment received.',
  `amount` int(11) NOT NULL COMMENT 'Amount in smallest possible increment (cents for USD).',
  `currency` varchar(3) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Three-letter ISO 4217 currency code.',
  `days` int(11) NOT NULL COMMENT 'Number of days of premium access granted.  -1 = lifetime',
  `invoice_payload` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `telegram_charge_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `provider_charge_id` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 86 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for users
-- ----------------------------
DROP TABLE IF EXISTS `users`;
CREATE TABLE `users`  (
  `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Auto-increment user ID',
  `type` enum('TELEGRAM_USER','UNKNOWN') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'UNKNOWN' COMMENT 'Type of user',
  `access_level` enum('BANNED','BASIC','PREMIUM','ADMIN') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'BASIC',
  `telegram_id` bigint(20) NULL DEFAULT NULL COMMENT 'Telegram user ID, or NULL if not a Telegram user',
  `username` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'User\'s username',
  `language` varchar(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
  `date_added` datetime NOT NULL,
  `date_last_seen` datetime NULL DEFAULT NULL,
  `date_terms_accepted` datetime NULL DEFAULT NULL COMMENT 'Date the user accepted the terms of service',
  `date_premium_expires` datetime NULL DEFAULT NULL,
  `lifetime_subscription` tinyint(1) NULL DEFAULT NULL COMMENT 'Was lifetime subscription purchased',
  PRIMARY KEY (`id`) USING BTREE,
  UNIQUE INDEX `telegram_id`(`telegram_id`) USING BTREE,
  INDEX `id`(`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 4639 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for worker_lora_tags
-- ----------------------------
DROP TABLE IF EXISTS `worker_lora_tags`;
CREATE TABLE `worker_lora_tags`  (
  `lora_id` int(11) NOT NULL,
  `worker_id` int(11) NOT NULL,
  `tag_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `frequency` int(11) NULL DEFAULT NULL,
  INDEX `idx1`(`lora_id`, `worker_id`, `tag_name`) USING BTREE,
  CONSTRAINT `worker_lora_tags_ibfk_1` FOREIGN KEY (`lora_id`) REFERENCES `worker_loras` (`lora_id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for worker_loras
-- ----------------------------
DROP TABLE IF EXISTS `worker_loras`;
CREATE TABLE `worker_loras`  (
  `lora_id` int(11) NOT NULL AUTO_INCREMENT,
  `worker_id` int(11) NULL DEFAULT NULL,
  `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `alias` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `path` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`lora_id`) USING BTREE,
  UNIQUE INDEX `worker_id`(`worker_id`, `name`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 17486 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for worker_models
-- ----------------------------
DROP TABLE IF EXISTS `worker_models`;
CREATE TABLE `worker_models`  (
  `worker_id` int(10) UNSIGNED NOT NULL,
  `model_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `model_hash` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `model_sha256` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `model_title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `model_filename` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `model_config` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`worker_id`, `model_name`) USING BTREE,
  INDEX `idx1`(`model_name`) USING BTREE,
  INDEX `idx2`(`model_hash`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for workers
-- ----------------------------
DROP TABLE IF EXISTS `workers`;
CREATE TABLE `workers`  (
  `id` int(10) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Worker ID number',
  `enabled` tinyint(1) UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Is the worker available for use',
  `online` tinyint(1) NOT NULL DEFAULT 0 COMMENT 'Is the worker currently online?',
  `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'User-friendly worker name',
  `url` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'URL to A1111 API',
  `max_img_size` int(11) NULL DEFAULT NULL COMMENT 'Maximum image size in pixels (width*height)',
  `max_img_steps` int(11) NULL DEFAULT NULL COMMENT 'Maximum number of steps allowed',
  `max_upscale_size` int(11) NULL DEFAULT NULL,
  `gpu_uuid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'Corresponding GPU UUID to match with gpu_stats',
  `date_started` datetime(3) NULL DEFAULT NULL COMMENT 'Date when worker was last started',
  `date_used` datetime(3) NULL DEFAULT NULL COMMENT 'Date last used for processing',
  `date_failed` datetime(3) NULL DEFAULT NULL COMMENT 'Date of last failure/error',
  `last_error` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'Last error message encountered',
  `last_queue_id` bigint(20) UNSIGNED NULL DEFAULT NULL COMMENT 'Last queue item processed',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 7 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

SET FOREIGN_KEY_CHECKS = 1;
