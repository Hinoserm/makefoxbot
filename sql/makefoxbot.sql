/*
 Navicat Premium Data Transfer

 Source Server         : Dextrose
 Source Server Type    : MariaDB
 Source Server Version : 100521
 Source Host           : localhost:3306
 Source Schema         : makefoxbot

 Target Server Type    : MariaDB
 Target Server Version : 100521
 File Encoding         : 65001

 Date: 10/02/2024 18:24:27
*/

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

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
  `image` longblob NOT NULL,
  `sha1hash` varchar(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `date_added` datetime NOT NULL,
  `telegram_fileid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'The Telegram file ID for the smaller, compressed image, once it\'s been uploaded.',
  `telegram_uniqueid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `telegram_full_fileid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'The Telegram file ID for the full-size uncompressed image, once it\'s been uploaded.',
  `telegram_full_uniqueid` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `telegram_chatid` bigint(20) NULL DEFAULT NULL,
  `telegram_msgid` bigint(20) NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `idx3`(`user_id`, `telegram_uniqueid`, `date_added`) USING BTREE,
  INDEX `idx1`(`user_id`) USING BTREE,
  INDEX `idx2`(`user_id`, `telegram_chatid`, `date_added`) USING BTREE,
  INDEX `idx4`(`type`) USING BTREE,
  INDEX `idx5`(`type`, `id`) USING BTREE,
  CONSTRAINT `images_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE = InnoDB AUTO_INCREMENT = 144900 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for prompts
-- ----------------------------
DROP TABLE IF EXISTS `prompts`;
CREATE TABLE `prompts`  (
  `key` bigint(20) NULL DEFAULT NULL,
  `value` blob NULL DEFAULT NULL
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for queue
-- ----------------------------
DROP TABLE IF EXISTS `queue`;
CREATE TABLE `queue`  (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `status` enum('PENDING','PROCESSING','SENDING','FINISHED','ERROR') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'FINISHED',
  `type` enum('TXT2IMG','IMG2IMG') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `uid` bigint(20) NOT NULL,
  `tele_id` bigint(20) NOT NULL,
  `tele_chatid` bigint(20) NOT NULL,
  `image_id` bigint(20) UNSIGNED NULL DEFAULT NULL,
  `steps` int(11) NULL DEFAULT NULL,
  `seed` int(11) NULL DEFAULT NULL,
  `cfgscale` decimal(6, 2) NULL DEFAULT NULL,
  `prompt` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `negative_prompt` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `width` int(11) NULL DEFAULT NULL,
  `height` int(11) NULL DEFAULT NULL,
  `denoising_strength` decimal(6, 2) NULL DEFAULT NULL,
  `selected_image` bigint(20) UNSIGNED NULL DEFAULT NULL,
  `reply_msg` bigint(20) NULL DEFAULT NULL,
  `msg_id` bigint(20) NULL DEFAULT NULL,
  `error_str` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `sent` tinyint(1) NOT NULL DEFAULT 0,
  `date_added` datetime(3) NOT NULL,
  `date_worker_start` datetime(3) NULL DEFAULT NULL,
  `date_sent` datetime(3) NULL DEFAULT NULL,
  `date_finished` datetime(3) NULL DEFAULT NULL,
  `date_failed` datetime(3) NULL DEFAULT NULL,
  `link_token` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `worker` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `idx1`(`status`) USING BTREE,
  INDEX `idx2`(`date_added`) USING BTREE,
  INDEX `idx3`(`date_failed`) USING BTREE,
  INDEX `idx4`(`status`, `date_failed`) USING BTREE,
  INDEX `idx5`(`link_token`) USING BTREE,
  INDEX `idx6`(`uid`) USING BTREE,
  INDEX `idx7`(`status`, `uid`) USING BTREE,
  INDEX `idx8`(`tele_id`, `tele_chatid`) USING BTREE,
  INDEX `idx9`(`status`, `date_added`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 142963 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

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
  PRIMARY KEY (`session_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_chats
-- ----------------------------
DROP TABLE IF EXISTS `telegram_chats`;
CREATE TABLE `telegram_chats`  (
  `id` bigint(20) NOT NULL,
  `type` enum('PRIVATE','GROUP','SUPERGROUP','CHANNEL','UNKNOWN') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'UNKNOWN',
  `username` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `firstname` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `lastname` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `bio` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `date_updated` datetime NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_log
-- ----------------------------
DROP TABLE IF EXISTS `telegram_log`;
CREATE TABLE `telegram_log`  (
  `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id` bigint(20) NOT NULL,
  `chat_id` bigint(20) NOT NULL,
  `message_text` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL,
  `date_added` datetime NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 160527 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for telegram_user_settings
-- ----------------------------
DROP TABLE IF EXISTS `telegram_user_settings`;
CREATE TABLE `telegram_user_settings`  (
  `uid` bigint(20) NOT NULL,
  `tele_id` bigint(20) NOT NULL,
  `tele_chatid` bigint(20) NOT NULL,
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
  `username` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `firstname` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `lastname` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  `is_premium` tinyint(1) NULL DEFAULT NULL,
  `date_updated` datetime NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for users
-- ----------------------------
DROP TABLE IF EXISTS `users`;
CREATE TABLE `users`  (
  `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Auto-increment user ID',
  `type` enum('TELEGRAM_USER','UNKNOWN') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'UNKNOWN' COMMENT 'Type of user',
  `access_level` enum('BASIC','PREMIUM','ADMIN','BANNED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'BASIC',
  `telegram_id` bigint(20) NULL DEFAULT NULL COMMENT 'Telegram user ID, or NULL if not a Telegram user',
  `username` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'User\'s username',
  `date_added` datetime NOT NULL,
  `date_last_seen` datetime NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `id`(`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 313 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci ROW_FORMAT = Dynamic;

SET FOREIGN_KEY_CHECKS = 1;