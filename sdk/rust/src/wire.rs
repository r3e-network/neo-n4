use serde::{Deserialize, Deserializer, de::Error};

fn parse_u64<E: Error>(value: String) -> Result<u64, E> {
    if value == "0" || (!value.starts_with('0') && value.bytes().all(|byte| byte.is_ascii_digit()))
    {
        return value.parse().map_err(E::custom);
    }
    Err(E::custom("u64 must be a canonical decimal JSON string"))
}

pub(crate) fn deserialize_u64<'de, D: Deserializer<'de>>(deserializer: D) -> Result<u64, D::Error> {
    parse_u64(String::deserialize(deserializer)?)
}

pub(crate) fn deserialize_optional_u64<'de, D: Deserializer<'de>>(
    deserializer: D,
) -> Result<Option<u64>, D::Error> {
    Option::<String>::deserialize(deserializer)?
        .map(parse_u64)
        .transpose()
}
