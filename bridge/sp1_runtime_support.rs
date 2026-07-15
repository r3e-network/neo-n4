#[cfg(any(
    test,
    all(unix, not(target_arch = "aarch64"), not(feature = "native-gnark"))
))]
pub const PINNED_SP1_GNARK_IMAGE: &str = "ghcr.io/succinctlabs/sp1-gnark@sha256:be8555f1ad90870acd8c6ec7fd3ba0b1a2133ea9cddf25e130665aa651129e54";

pub fn validate_gnark_backend() -> Result<(), String> {
    #[cfg(all(unix, target_arch = "aarch64", not(feature = "native-gnark")))]
    {
        Err(
            "the SP1 6.2.1 gnark image is amd64-only; aarch64 prover hosts must enable the upstream native-gnark feature"
                .into(),
        )
    }

    #[cfg(all(unix, not(target_arch = "aarch64"), not(feature = "native-gnark")))]
    {
        let image = std::env::var("SP1_GNARK_IMAGE").ok();
        validate_docker_image(image.as_deref())
    }

    #[cfg(any(not(unix), feature = "native-gnark"))]
    {
        Ok(())
    }
}

#[cfg(any(
    test,
    all(unix, not(target_arch = "aarch64"), not(feature = "native-gnark"))
))]
fn validate_docker_image(image: Option<&str>) -> Result<(), String> {
    match image {
        Some(PINNED_SP1_GNARK_IMAGE) => Ok(()),
        Some(actual) => Err(format!(
            "SP1_GNARK_IMAGE must equal the audited immutable image {PINNED_SP1_GNARK_IMAGE}, got {actual}"
        )),
        None => Err(format!(
            "SP1_GNARK_IMAGE must be set to the audited immutable image {PINNED_SP1_GNARK_IMAGE}"
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn exact_gnark_image_digest_is_accepted() {
        assert_eq!(validate_docker_image(Some(PINNED_SP1_GNARK_IMAGE)), Ok(()));
    }

    #[test]
    fn mutable_or_missing_gnark_image_is_rejected() {
        assert!(validate_docker_image(None).is_err());
        assert!(validate_docker_image(Some("ghcr.io/succinctlabs/sp1-gnark:v6.1.0")).is_err());
    }
}
