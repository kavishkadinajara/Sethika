CREATE TABLE booking.GratitudePostPhotos (
    GratitudePostId BIGINT   NOT NULL REFERENCES booking.GratitudePosts(Id) ON DELETE CASCADE,
    FileId          BIGINT   NOT NULL REFERENCES core.Files(Id),
    SortOrder       TINYINT  NOT NULL DEFAULT 0,
    PRIMARY KEY (GratitudePostId, FileId)
);
GO
