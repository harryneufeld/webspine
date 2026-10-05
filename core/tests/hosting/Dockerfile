ARG PHP_BASE=php:8.3-fpm-bookworm
FROM ${PHP_BASE}
RUN apt-get update && apt-get install -y --no-install-recommends libzip-dev libsqlite3-dev \
    && docker-php-ext-install pdo_sqlite zip opcache \
    && rm -rf /var/lib/apt/lists/* \
    && if command -v a2enmod >/dev/null; then a2enmod rewrite; fi
COPY opcache.ini /usr/local/etc/php/conf.d/webspine-test.ini
