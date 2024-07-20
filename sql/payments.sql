-- pay_invoices table
CREATE TABLE `pay_invoices` (
  `uuid` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL DEFAULT uuid() COMMENT 'UUID for this payment invoice',
  `user_id` BIGINT(20) UNSIGNED NOT NULL COMMENT 'User ID number',
  `amount` DECIMAL(10, 2) NULL DEFAULT NULL COMMENT 'Amount of transaction in smallest currency unit',
  `currency` CHAR(3) CHARACTER SET ascii COLLATE ascii_general_ci NULL DEFAULT NULL COMMENT 'Currency expected for this transaction in ISO 4217 format',
  `days` INT(11) NULL DEFAULT NULL COMMENT 'Days of membership expected; null if this is another type of payment',
  `recurring` TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'Is this a recurring payment?',
  `date_created` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date this invoice was created',
  `date_charged` DATETIME NULL DEFAULT NULL COMMENT 'Date this invoice was last successfully billed',
  `date_last_failed` DATETIME NULL DEFAULT NULL COMMENT 'Date this invoice last failed to bill',
  `last_error` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'Last error message encountered, if any',
  PRIMARY KEY (`uuid`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic COMMENT='Table storing invoice details';

-- pay_charges table
CREATE TABLE `pay_charges` (
  `charge_id` BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Auto-incrementing charge ID',
  `invoice_id` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT 'UUID of the related invoice',
  `user_id` BIGINT(20) UNSIGNED NOT NULL COMMENT 'User ID number',
  `provider` ENUM('PAYPAL','STRIPE','OTHER') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Payment provider',
  `token` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Token provided by payment provider',
  `status` ENUM('PENDING', 'COMPLETED', 'FAILED') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Status of the charge',
  `date_created` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date the charge was created',
  `date_completed` DATETIME NULL DEFAULT NULL COMMENT 'Date the charge was completed',
  `error_message` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL COMMENT 'Error message encountered, if any',
  PRIMARY KEY (`charge_id`),
  FOREIGN KEY (`invoice_id`) REFERENCES `pay_invoices`(`uuid`) ON DELETE CASCADE ON UPDATE RESTRICT,
  FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT='Table storing charge details';

-- pay_customers table
CREATE TABLE `pay_customers` (
  `customer_id` BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Auto-incrementing customer ID',
  `user_id` BIGINT(20) UNSIGNED NOT NULL COMMENT 'User ID number',
  `provider` ENUM('PAYPAL','STRIPE','OTHER') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Payment provider',
  `customer_token` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Customer token provided by payment provider',
  `date_created` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date the customer was created',
  PRIMARY KEY (`customer_id`),
  UNIQUE INDEX `user_id`(`user_id`),
  FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT='Table storing customer details';

-- pay_subscriptions table
CREATE TABLE `pay_subscriptions` (
  `subscription_id` BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Auto-incrementing subscription ID',
  `customer_id` BIGINT(20) UNSIGNED NOT NULL COMMENT 'Customer ID number',
  `invoice_id` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT 'UUID of the related invoice',
  `user_id` BIGINT(20) UNSIGNED NOT NULL COMMENT 'User ID number',
  `provider` ENUM('PAYPAL','STRIPE','OTHER') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Payment provider',
  `subscription_token` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Subscription token provided by payment provider',
  `amount` DECIMAL(10, 2) NOT NULL COMMENT 'Subscription amount in smallest currency unit',
  `currency` CHAR(3) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL COMMENT 'Currency in ISO 4217 format',
  `interval_days` INT(11) NOT NULL COMMENT 'Number of days between each billing cycle',
  `status` ENUM('ACTIVE', 'CANCELLED', 'PAUSED') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Status of the subscription',
  `date_created` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date the subscription was created',
  `date_updated` DATETIME NULL DEFAULT NULL COMMENT 'Date the subscription was last updated',
  PRIMARY KEY (`subscription_id`),
  FOREIGN KEY (`customer_id`) REFERENCES `pay_customers`(`customer_id`) ON DELETE CASCADE ON UPDATE RESTRICT,
  FOREIGN KEY (`invoice_id`) REFERENCES `pay_invoices`(`uuid`) ON DELETE CASCADE ON UPDATE RESTRICT,
  FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT='Table storing subscription details';

-- pay_transactions table
CREATE TABLE `pay_transactions` (
  `transaction_id` BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'Auto-incrementing transaction ID',
  `charge_id` BIGINT(20) UNSIGNED NULL DEFAULT NULL COMMENT 'Linked charge ID',
  `subscription_id` BIGINT(20) UNSIGNED NULL DEFAULT NULL COMMENT 'Linked subscription ID',
  `user_id` BIGINT(20) UNSIGNED NOT NULL COMMENT 'User ID number',
  `amount` DECIMAL(10, 2) NOT NULL COMMENT 'Transaction amount in smallest currency unit',
  `currency` VARCHAR(3) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Currency in ISO 4217 format',
  `status` ENUM('PENDING', 'COMPLETED', 'FAILED') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Status of the transaction',
  `date_created` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Date the transaction was created',
  `date_completed` DATETIME NULL DEFAULT NULL COMMENT 'Date the transaction was completed',
  PRIMARY KEY (`transaction_id`),
  FOREIGN KEY (`charge_id`) REFERENCES `pay_charges`(`charge_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  FOREIGN KEY (`subscription_id`) REFERENCES `pay_subscriptions`(`subscription_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT='Table storing transaction details';
