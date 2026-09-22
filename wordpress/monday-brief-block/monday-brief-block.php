<?php
/**
 * Plugin Name:          Monday Brief Block
 * Description:          Shows the latest weekly business brief and headline KPIs from a Monday Brief API.
 * Version:              0.2.0
 * Requires at least:    6.5
 * Requires PHP:         8.1
 * Author:               Michael Reynolds
 * License:              GPL-2.0-or-later
 * License URI:          http://www.gnu.org/licenses/gpl-2.0.html
 * Text Domain:          monday-brief-block
 *
 * @package MondayBriefBlock
 */

declare(strict_types=1);

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

define( 'MONDAY_BRIEF_BLOCK_VERSION', '0.2.0' );
define( 'MONDAY_BRIEF_BLOCK_DIR', plugin_dir_path( __FILE__ ) );
define( 'MONDAY_BRIEF_BLOCK_OPTION', 'monday_brief_block_settings' );
define( 'MONDAY_BRIEF_BLOCK_CACHE', 'monday_brief_block_summary' );
define( 'MONDAY_BRIEF_BLOCK_FAILURE', 'monday_brief_block_failure' );
define( 'MONDAY_BRIEF_BLOCK_LAST_GOOD', 'monday_brief_block_last_good' );

require_once MONDAY_BRIEF_BLOCK_DIR . 'includes/settings.php';
require_once MONDAY_BRIEF_BLOCK_DIR . 'includes/client.php';

add_action(
	'init',
	static function (): void {
		register_block_type( MONDAY_BRIEF_BLOCK_DIR . 'block' );
	}
);
