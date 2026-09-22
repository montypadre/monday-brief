<?php
/**
 * Settings page: API URL, API key and cache length, stored with the Options API.
 *
 * @package MondayBriefBlock
 */

declare(strict_types=1);

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Saved settings merged over defaults.
 *
 * @return array{api_url: string, api_key: string, cache_minutes: int}
 */
function mbb_settings(): array {
	$defaults = array(
		'api_url'       => '',
		'api_key'       => '',
		'cache_minutes' => 60,
	);

	$saved = get_option( MONDAY_BRIEF_BLOCK_OPTION, array() );

	return wp_parse_args( is_array( $saved ) ? $saved : array(), $defaults );
}

add_action(
	'admin_menu',
	static function (): void {
		add_options_page(
			__( 'Monday Brief', 'monday-brief-block' ),
			__( 'Monday Brief', 'monday-brief-block' ),
			'manage_options',
			'monday-brief',
			'mbb_render_settings_page'
		);
	}
);

add_action(
	'admin_init',
	static function (): void {
		register_setting(
			'monday_brief',
			MONDAY_BRIEF_BLOCK_OPTION,
			array(
				'type'              => 'array',
				'sanitize_callback' => 'mbb_sanitize_settings',
				'default'           => array(),
			)
		);

		add_settings_section( 'mbb_connection', __( 'Connection', 'monday-brief-block' ), '__return_false', 'monday-brief' );

		add_settings_field( 'api_url', __( 'API URL', 'monday-brief-block' ), 'mbb_field_api_url', 'monday-brief', 'mbb_connection', array( 'label_for' => 'mbb_api_url' ) );
		add_settings_field( 'api_key', __( 'API key', 'monday-brief-block' ), 'mbb_field_api_key', 'monday-brief', 'mbb_connection', array( 'label_for' => 'mbb_api_key' ) );
		add_settings_field( 'cache_minutes', __( 'Cache for (minutes)', 'monday-brief-block' ), 'mbb_field_cache_minutes', 'monday-brief', 'mbb_connection', array( 'label_for' => 'mbb_cache_minutes' ) );
	}
);

/**
 * Validates input. A blank key field keeps the saved key, because the saved key is never printed into the form.
 *
 * @param mixed $input Raw submitted values.
 * @return array{api_url: string, api_key: string, cache_minutes: int}
 */
function mbb_sanitize_settings( $input ): array {
	$current = mbb_settings();
	$input   = is_array( $input ) ? $input : array();

	$url     = isset( $input['api_url'] ) ? esc_url_raw( trim( (string) $input['api_url'] ) ) : '';
	$key     = isset( $input['api_key'] ) ? sanitize_text_field( (string) $input['api_key'] ) : '';
	$minutes = isset( $input['cache_minutes'] ) ? absint( $input['cache_minutes'] ) : 60;

	if ( '' === $key ) {
		$key = $current['api_key'];
	}

	// New settings mean the cached copy may be from the wrong API.
	delete_transient( MONDAY_BRIEF_BLOCK_CACHE );
	delete_transient( MONDAY_BRIEF_BLOCK_FAILURE );

	return array(
		'api_url'       => untrailingslashit( $url ),
		'api_key'       => $key,
		'cache_minutes' => max( 1, min( 1440, $minutes ) ),
	);
}

/** Renders the API URL field */
function mbb_field_api_url(): void {
	printf(
		'<input type="url" id="mbb_api_url" name="%s[api_url]" value="%s" class="regular-text" placeholder="https://example.com" />',
		esc_attr( MONDAY_BRIEF_BLOCK_OPTION ),
		esc_attr( mbb_settings()['api_url'] )
	);
	echo '<p class="description">' . esc_html__( 'The base address of the Monday Brief API, without a trailing slash.', 'monday-brief-block' ) . '</p>';
}

/** Renders the API key field. The saved key is never echoed back. */
function mbb_field_api_key(): void {
	$has_key = '' !== mbb_settings()['api_key'];

	printf(
		'<input type="password" id="mbb_api_key" name="%s[api_key]" value="" class="regular-text" autocomplete="off" placeholder="%s" />',
		esc_attr( MONDAY_BRIEF_BLOCK_OPTION ),
		esc_attr( $has_key ? __( 'Saved. Leave blank to keep it.', 'monday-brief-block' ) : '' )
	);
	echo '<p class="description">' . esc_html__( 'Sent server-to-server only. It never reaches visitors\' browsers.', 'monday-brief-block' ) . '</p>';
}

/** Renders the cache length field. */
function mbb_field_cache_minutes(): void {
	printf(
		'<input type="number" id="mbb_cache_minutes" name="%s[cache_minutes]" value="%d" min="1" max="1440" class="small-text" />',
		esc_attr( MONDAY_BRIEF_BLOCK_OPTION ),
		absint( mbb_settings()['cache_minutes'] )
	);
}

/** Renders the settings page, with a live connection check underneath. */
function mbb_render_settings_page(): void {
	if ( ! current_user_can( 'manage_options' ) ) {
		return;
	}

	$result = mbb_fetch_summary();
	?>
	<div class="wrap">
		<h1><?php echo esc_html( get_admin_page_title() ); ?></h1>

		<form action="options.php" method="post">
			<?php
			settings_fields( 'monday_brief' );
			do_settings_sections( 'monday-brief' );
			submit_button();
			?>
		</form>

		<h2><?php esc_html_e( 'Connection status', 'monday-brief-block' ); ?></h2>
		<?php if ( $result['ok'] && ! $result['stale'] ) : ?>
			<div class="notice notice-success inline">
				<p>
					<?php
					printf(
						/* translators: %s: date the brief's week starts. */
						esc_html__( 'Connected. Latest brief is for the week of %s.', 'monday-brief-block' ),
						esc_html( mbb_format_date( $result['data']['weekStart'] ?? '' ) )
					);
					?>
				</p>
			</div>
		<?php else : ?>
			<div class="notice notice-warning inline">
				<p><?php echo esc_html( (string) $result['error'] ); ?></p>
			</div>
		<?php endif; ?>
	</div>
	<?php
}
