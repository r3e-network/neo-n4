#!/bin/bash
# Generate SHA256 hash for domain strings
echo -n "neo-n4-testnet-fraud" | sha256sum | cut -d' ' -f1
echo -n "neo-n4-testnet-gateway" | sha256sum | cut -d' ' -f1
