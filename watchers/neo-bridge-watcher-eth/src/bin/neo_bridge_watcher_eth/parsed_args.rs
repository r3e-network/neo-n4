use std::path::PathBuf;

pub(crate) struct ParsedArgs {
    pub(crate) config_path: PathBuf,
    pub(crate) preflight: bool,
    pub(crate) journal_info: bool,
    pub(crate) allow_stub_signer: bool,
}
