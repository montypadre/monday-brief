<?php
/**
 * Removes everything the plugin stored.
 * 
 * @package MondayBriefBlock
 */

if ( ! defined( 'WP_UNINSTALL_PLUGIN' ) ) {
    exit;
}

delete_option( 'monday_brief_block_settings' );
delete_option( 'monday_brief_block_last_good' );
delete_transient( 'monday_brief_block_summary' );
delete_transient( 'monday_brief_block_failure' );