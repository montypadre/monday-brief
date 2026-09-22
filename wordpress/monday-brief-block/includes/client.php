<?php
/**
 * Fetches and caches the summary from the Monday Brief API.
 *
 * @package MondayBriefBlock
 */

declare(strict_types=1);

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * The summary, from cache when possible.
 *
 * On failure, falls back to the last good response so a page never breaks because the API is down,
 * and records the failure briefly so a dead API isn't called on every page view.
 *
 * @return array{ok: bool, data: array|null, error: string|null, stale: bool}
 */
function mbb_fetch_summary(): array {
	$cached = get_transient( MONDAY_BRIEF_BLOCK_CACHE );
	if ( is_array( $cached ) ) {
		return array(
			'ok'    => true,
			'data'  => $cached,
			'error' => null,
			'stale' => false,
		);
	}

	$recent_failure = get_transient( MONDAY_BRIEF_BLOCK_FAILURE );
	if ( is_string( $recent_failure ) ) {
		return mbb_fallback( $recent_failure );
	}

	$settings = mbb_settings();
	if ( '' === $settings['api_url'] || '' === $settings['api_key'] ) {
		return mbb_fallback( __( 'The API URL and key are not set yet. Add them under Settings -> Monday Brief.', 'monday-brief-block' ) );
	}

	$response = wp_remote_get(
		$settings['api_url'] . '/api/wp/summary',
		array(
			'timeout' => 8,
			'headers' => array(
				'X-Api-Key' => $settings['api_key'],
				'Accept'    => 'application/json',
			),
		)
	);

	if ( is_wp_error( $response ) ) {
		/* translators: %s: error message from the HTTP request. */
		return mbb_fail( sprintf( __( 'Could not reach the Monday Brief API: %s', 'monday-brief-block' ), $response->get_error_message() ) );
	}

	$status = (int) wp_remote_retrieve_response_code( $response );
	if ( 401 === $status ) {
		return mbb_fail( __( 'The Monday Brief API rejected the key.', 'monday-brief-block' ) );
	}

	if ( 200 !== $status ) {
		/* translators: %d: HTTP status code. */
		return mbb_fail( sprintf( __( 'The Monday Brief API returned HTTP %d.', 'monday-brief-block' ), $status ) );
	}

	$data = json_decode( wp_remote_retrieve_body( $response ), true );
	if ( ! is_array( $data ) || empty( $data['cards'] ) || ! is_array( $data['cards'] ) ) {
		return mbb_fail( __( 'The Monday Brief API returned a response in an unexpected shape.', 'monday-brief-block' ) );
	}

	set_transient( MONDAY_BRIEF_BLOCK_CACHE, $data, $settings['cache_minutes'] * MINUTE_IN_SECONDS );
	update_option( MONDAY_BRIEF_BLOCK_LAST_GOOD, $data, false );

	return array(
		'ok'    => true,
		'data'  => $data,
		'error' => null,
		'stale' => false,
	);
}

/**
 * Records a failure for one minute, then falls back.
 *
 * @param string $error What went wrong, in plain words.
 * @return array{ok: bool, data: array|null, error: string|null, stale: bool}
 */
function mbb_fail( string $error ): array {
	set_transient( MONDAY_BRIEF_BLOCK_FAILURE, $error, MINUTE_IN_SECONDS );
	return mbb_fallback( $error );
}

/**
 * The last good response, if there is one.
 *
 * @param string $error What went wrong.
 * @return array{ok: bool, data: array|null, error: string|null, stale: bool}
 */
function mbb_fallback( string $error ): array {
	$last = get_option( MONDAY_BRIEF_BLOCK_LAST_GOOD );

	return array(
		'ok'    => is_array( $last ),
		'data'  => is_array( $last ) ? $last : null,
		'error' => $error,
		'stale' => is_array( $last ),
	);
}

/**
 * Formats a card value the same way the dashboard does.
 *
 * @param mixed  $value The number.
 * @param string $format currency, percent or number.
 */
function mbb_format_value( $value, string $format ): string {
	$number = (float) $value;

	return match ( $format ) {
		'currency' => '$' . number_format( $number, 2 ),
		'percent'  => rtrim( rtrim( number_format( $number, 2 ), '0' ), '.' ) . '%',
		default    => number_format( $number ),
	};
}

/**
 * Describes a change in words and a symbol, never color alone.
 *
 * @param array $card One card from the API.
 * @return array{text: string, direction: string}
 */
function mbb_format_delta( array $card ): array {
	if ( ! isset( $card['changePct'] ) || null === $card['changePct'] ) {
		return array(
			'text'      => __( 'no earlier period to compare', 'monday-brief-block' ),
			'direction' => 'flat',
		);
	}

	$change    = (float) $card['changePct'];
	$direction = $change > 0 ? 'up' : ( $change < 0 ? 'down' : 'flat' );
	$symbol    = 'up' === $direction ? '▲' : ( 'down' === $direction ? '▼' : '–' );
	$points    = isset( $card['changePoints'] ) && null !== $card['changePoints']
		/* translators: %s: change in percentage points. */
		? ' ' . sprintf( __( '(%s points)', 'monday-brief-block' ), ( (float) $card['changePoints'] > 0 ? '+' : '' ) . $card['changePoints'] )
		: '';

	return array(
		/* translators: 1: arrow symbol, 2: up or down, 3: size of the change, 4: optional points. */
		'text'      => sprintf( __( '%1$s %2$s %3$s%%%4$s on the period before', 'monday-brief-block' ), $symbol, $direction, abs( $change ), $points ),
		'direction' => $direction,
	);
}

/**
 * A date the reader can read, in the site's date format.
 *
 * @param string $iso Date as YYYY-MM-DD.
 */
function mbb_format_date( string $iso ): string {
	$timestamp = strtotime( $iso . ' 00:00:00 UTC' );
	return false === $timestamp ? $iso : wp_date( get_option( 'date_format' ), $timestamp, new DateTimeZone( 'UTC' ) );
}
